using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;
using TMPro;

public class User : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float fastMoveMultiplier = 3f;
    [SerializeField] private float mouseSensitivity = 0.1f;

    [Header("Command")]
    [SerializeField] private string backendWebSocketUrl = "ws://localhost:8000/ws/agent";
    [SerializeField] private bool connectOnStart = true;
    [SerializeField] private TMP_InputField commandInputField;
    [SerializeField] private act_npc_controller npcController;

    private readonly ConcurrentQueue<Action> mainThreadActions = new ConcurrentQueue<Action>();
    private ClientWebSocket webSocket;
    private CancellationTokenSource webSocketCancellation;
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

    private void Start()
    {
        if (connectOnStart)
        {
            ConnectToBackend();
        }
    }

    private void OnEnable()
    {
        if (commandInputField != null)
        {
            commandInputField.onSubmit.AddListener(SubmitCommandFromInput);
        }
    }

    private async void OnDisable()
    {
        if (commandInputField != null)
        {
            commandInputField.onSubmit.RemoveListener(SubmitCommandFromInput);
        }

        await CloseBackendConnection();
    }

    private void Update()
    {
        DrainMainThreadActions();

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

    public async void ConnectToBackend()
    {
        await ConnectToBackendAsync();
    }

    private async Task<bool> ConnectToBackendAsync()
    {
        if (webSocket != null && webSocket.State == WebSocketState.Open)
        {
            return true;
        }

        webSocketCancellation?.Cancel();
        webSocketCancellation = new CancellationTokenSource();
        webSocket = new ClientWebSocket();

        try
        {
            await webSocket.ConnectAsync(new Uri(backendWebSocketUrl), webSocketCancellation.Token);
            Debug.Log($"Backend WebSocket connected: {backendWebSocketUrl}");
            _ = ReceiveBackendMessages(webSocketCancellation.Token);
            return true;
        }
        catch (Exception exc)
        {
            Debug.LogError($"Backend WebSocket connection failed: {exc.Message}");
            return false;
        }
    }

    public async void SendCommandToBackend(string commandMessage)
    {
        if (string.IsNullOrWhiteSpace(commandMessage))
        {
            Debug.LogError("Backend command request failed: message is required.");
            return;
        }

        if (webSocket == null || webSocket.State != WebSocketState.Open)
        {
            bool connected = await ConnectToBackendAsync();
            if (!connected)
            {
                Debug.LogError("Backend command request failed: WebSocket connection is not available.");
                return;
            }
        }

        await SendText(commandMessage);
        Debug.Log($"Sent command message: {commandMessage}");
    }

    private async Task ReceiveBackendMessages(CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[4096];

        try
        {
            while (!cancellationToken.IsCancellationRequested && webSocket.State == WebSocketState.Open)
            {
                MemoryStream messageStream = new MemoryStream();
                WebSocketReceiveResult result;

                try
                {
                    do
                    {
                        result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);

                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            await CloseBackendConnection();
                            return;
                        }

                        messageStream.Write(buffer, 0, result.Count);
                    }
                    while (!result.EndOfMessage);

                    string message = Encoding.UTF8.GetString(messageStream.ToArray());
                    mainThreadActions.Enqueue(() => HandleBackendMessage(message));
                }
                finally
                {
                    messageStream.Dispose();
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exc)
        {
            Debug.LogError($"Backend WebSocket receive failed: {exc.Message}");
        }
    }

    private void HandleBackendMessage(string responseJson)
    {
        BackendMessageEnvelope envelope;

        try
        {
            envelope = JsonUtility.FromJson<BackendMessageEnvelope>(responseJson);
        }
        catch (Exception exc)
        {
            Debug.LogError($"Backend WebSocket message parse failed: {exc.Message}");
            return;
        }

        if (envelope == null || string.IsNullOrWhiteSpace(envelope.type))
        {
            Debug.LogError($"Backend WebSocket message did not include a type: {responseJson}");
            return;
        }

        switch (envelope.type)
        {
            case "client_function_call":
                HandleClientFunctionCall(responseJson);
                break;
            case "final_command":
                HandleBackendCommandResponse(responseJson);
                break;
            case "error":
                Debug.LogError($"Backend error message: {responseJson}");
                break;
            default:
                Debug.LogWarning($"Unsupported backend WebSocket message type: {envelope.type}");
                break;
        }
    }

    private void HandleClientFunctionCall(string responseJson)
    {
        ClientFunctionCall call = JsonUtility.FromJson<ClientFunctionCall>(responseJson);
        ClientFunctionResult result = new ClientFunctionResult
        {
            type = "client_function_result",
            call_id = call.call_id,
            result = new act_npc_controller.ClientFunctionResult()
        };

        if (npcController == null)
        {
            result.result.ok = false;
            result.result.error = CreateFunctionError("NPC_CONTROLLER_NOT_ASSIGNED", "NPC controller is not assigned.");
            SendJson(JsonUtility.ToJson(result));
            return;
        }

        if (!npcController.TryHandleClientFunction(call.function, call.args, out act_npc_controller.ClientFunctionResult functionResult))
        {
            result.result = functionResult;
            SendJson(JsonUtility.ToJson(result));
            return;
        }

        result.result = functionResult;
        SendJson(JsonUtility.ToJson(result));
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

        if (!string.IsNullOrWhiteSpace(response.command.message))
        {
            Debug.Log($"LLM response: {response.command.message}");
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

    private async void SendJson(string json)
    {
        await SendText(json);
    }

    private async Task SendText(string text)
    {
        if (webSocket == null || webSocket.State != WebSocketState.Open)
        {
            Debug.LogError("Backend WebSocket send failed: socket is not connected.");
            return;
        }

        byte[] bytes = Encoding.UTF8.GetBytes(text);
        await webSocket.SendAsync(
            new ArraySegment<byte>(bytes),
            WebSocketMessageType.Text,
            true,
            webSocketCancellation.Token
        );
    }

    private async Task CloseBackendConnection()
    {
        webSocketCancellation?.Cancel();

        if (webSocket != null)
        {
            try
            {
                if (webSocket.State == WebSocketState.Open || webSocket.State == WebSocketState.CloseReceived)
                {
                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Unity client closing", CancellationToken.None);
                }
            }
            catch (Exception exc)
            {
                Debug.LogWarning($"Backend WebSocket close failed: {exc.Message}");
            }

            webSocket.Dispose();
            webSocket = null;
        }

        webSocketCancellation?.Dispose();
        webSocketCancellation = null;
    }

    private void DrainMainThreadActions()
    {
        while (mainThreadActions.TryDequeue(out Action action))
        {
            action.Invoke();
        }
    }

    private static act_npc_controller.ClientFunctionError CreateFunctionError(string code, string message)
    {
        return new act_npc_controller.ClientFunctionError
        {
            code = code,
            message = message
        };
    }

    private static float NormalizePitch(float angle)
    {
        return angle > 180f ? angle - 360f : angle;
    }

    [Serializable]
    private class BackendMessageEnvelope
    {
        public string type;
    }

    [Serializable]
    private class ClientFunctionCall
    {
        public string type;
        public string call_id;
        public string function;
        public act_npc_controller.ClientFunctionArgs args;
    }

    [Serializable]
    private class ClientFunctionResult
    {
        public string type;
        public string call_id;
        public act_npc_controller.ClientFunctionResult result;
    }

    [Serializable]
    private class CommandBackendResponse
    {
        public string type;
        public string status;
        public string input;
        public act_npc_controller.NpcCommand command;
    }
}
