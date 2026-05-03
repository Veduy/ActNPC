using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Networking;
using TMPro;

public class User : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float fastMoveMultiplier = 3f;
    [SerializeField] private float mouseSensitivity = 0.1f;

    [Header("Command")]
    [SerializeField] private string backendCommandUrl = "http://localhost:8000/command";
    [SerializeField] private TMP_InputField commandInputField;
    [SerializeField] private act_npc_controller npcController;

    private float yaw;
    private float pitch;

    private void Awake()
    {
        Vector3 rotation = transform.eulerAngles;
        yaw = rotation.y;
        pitch = NormalizePitch(rotation.x);

        if (npcController == null)
        {
            npcController = FindFirstObjectByType<act_npc_controller>();
        }

        if (commandInputField == null)
        {
            commandInputField = FindFirstObjectByType<TMP_InputField>();
        }
    }

    private void OnEnable()
    {
        if (commandInputField != null)
        {
            commandInputField.onSubmit.AddListener(SubmitCommandFromInput);
        }
    }

    private void OnDisable()
    {
        if (commandInputField != null)
        {
            commandInputField.onSubmit.RemoveListener(SubmitCommandFromInput);
        }
    }

    private void Update()
    {
        Mouse mouse = Mouse.current;
        Keyboard keyboard = Keyboard.current;

        if (mouse == null || keyboard == null)
        {
            return;
        }

        if (mouse.rightButton.wasPressedThisFrame)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        if (mouse.rightButton.wasReleasedThisFrame)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        if (!mouse.rightButton.isPressed)
        {
            return;
        }

        LookAround(mouse);
        MoveFreely(keyboard);
    }

    private void SubmitCommandFromInput(string commandMessage)
    {
        if (string.IsNullOrWhiteSpace(commandMessage))
        {
            return;
        }

        Debug.Log("Send Command Message");
        SendCommandToBackend(commandMessage.Trim());

        commandInputField.text = string.Empty;
        commandInputField.ActivateInputField();
    }

    private void LookAround(Mouse mouse)
    {
        Vector2 mouseDelta = mouse.delta.ReadValue();

        yaw += mouseDelta.x * mouseSensitivity;
        pitch -= mouseDelta.y * mouseSensitivity;
        pitch = Mathf.Clamp(pitch, -89f, 89f);

        transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
    }

    private void MoveFreely(Keyboard keyboard)
    {
        Vector3 direction = Vector3.zero;

        if (keyboard.wKey.isPressed)
        {
            direction += transform.forward;
        }
        if (keyboard.sKey.isPressed)
        {
            direction -= transform.forward;
        }
        if (keyboard.dKey.isPressed)
        {
            direction += transform.right;
        }
        if (keyboard.aKey.isPressed)
        {
            direction -= transform.right;
        }
        if (keyboard.eKey.isPressed)
        {
            direction += Vector3.up;
        }
        if (keyboard.qKey.isPressed)
        {
            direction += Vector3.down;
        }

        if (direction.sqrMagnitude > 1f)
        {
            direction.Normalize();
        }

        float speed = moveSpeed;
        if (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed)
        {
            speed *= fastMoveMultiplier;
        }

        transform.position += direction * speed * Time.deltaTime;
    }

    public void SendCommandToBackend(string commandMessage)
    {
        StartCoroutine(PostCommandToBackend(commandMessage));
    }

    private IEnumerator PostCommandToBackend(string commandMessage)
    {
        if (string.IsNullOrWhiteSpace(commandMessage))
        {
            Debug.LogError("Backend command request failed: message is required.");
            yield break;
        }

        CommandRequest payload = new CommandRequest
        {
            message = commandMessage
        };

        string json = JsonUtility.ToJson(payload);
        byte[] body = Encoding.UTF8.GetBytes(json);

        using UnityWebRequest request = new UnityWebRequest(backendCommandUrl, UnityWebRequest.kHttpVerbPOST);
        request.uploadHandler = new UploadHandlerRaw(body);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json; charset=utf-8");
        request.SetRequestHeader("Accept", "application/json");

        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"Backend command request failed: {request.responseCode} {request.error}");
            yield break;
        }

        HandleBackendCommandResponse(request.downloadHandler.text);
    }

    private void HandleBackendCommandResponse(string responseJson)
    {
        CommandBackendResponse response;

        try
        {
            response = JsonUtility.FromJson<CommandBackendResponse>(responseJson);
        }
        catch (Exception exc)
        {
            Debug.LogError($"Backend command response parse failed: {exc.Message}");
            return;
        }

        if (response == null)
        {
            Debug.LogError("Backend command response parse failed: response is empty.");
            return;
        }

        if (!string.Equals(response.status, "ok", StringComparison.OrdinalIgnoreCase))
        {
            Debug.LogError($"Backend command failed: status={response.status}");
            return;
        }

        if (response.command == null)
        {
            Debug.LogError("Backend command response did not include a command.");
            return;
        }

        if (npcController == null)
        {
            Debug.LogError("NPC controller is not assigned.");
            return;
        }

        if (!npcController.TryAct(response.command, out string actMessage))
        {
            Debug.LogWarning($"NPC command was not executed: {actMessage}");
            return;
        }

        Debug.Log($"NPC command handled: {actMessage}");
    }

    private static float NormalizePitch(float angle)
    {
        return angle > 180f ? angle - 360f : angle;
    }

    [Serializable]
    private class CommandRequest
    {
        public string message;
    }

    [Serializable]
    private class CommandBackendResponse
    {
        public string status;
        public string input;
        public act_npc_controller.NpcCommand command;
    }
}
