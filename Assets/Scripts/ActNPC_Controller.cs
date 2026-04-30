using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.InputSystem;
using UnityEngine.AI;

public class act_npc_controller : MonoBehaviour
{
    [SerializeField] private string backendCommandUrl = "http://localhost:8000/command";
    [TextArea]
    [SerializeField] private string testCommandMessage = "\uC0AC\uACFC\uB97C \uAC00\uC838\uC640";
    [SerializeField] private Rigidbody rb;

    [SerializeField] private Transform destination;
    private GameObject item;
    private NavMeshAgent navAgent;
    private bool hasActiveDestination;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>(); 
        navAgent = GetComponent<NavMeshAgent>();
    }
    
    private void Start()
    {
        
    }

    private void Update()
    {
        if (Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            Debug.Log("Send Command Message");
            SendCommandToBackend(testCommandMessage);
        }

        if(HasArrived())
        {
            Debug.Log("Arrived destination!");
            hasActiveDestination = false;
        }
    }


    public bool TryAct(NpcCommand command, out string message)
    {
        if (command == null)
        {
            message = "NPC command is required.";
            return false;
        }

        string action = NormalizeAction(command.action);

        if (string.IsNullOrWhiteSpace(action))
        {
            message = string.IsNullOrWhiteSpace(command.message)
                ? "No NPC action was requested."
                : command.message;
            Debug.Log($"NPC response: actor={gameObject.name}, message={message}");
            return true;
        }

        switch (action)
        {
            case "fetch":
                return TryFetch(FirstNonEmpty(command.item, command.destination), out message);
            case "move":
                return TryMoveTo(FirstNonEmpty(command.destination, command.item), out message);
            default:
                message = $"Unsupported NPC action: {command.action}";
                return false;
        }
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

        if (!TryAct(response.command, out string actMessage))
        {
            Debug.LogWarning($"NPC command was not executed: {actMessage}");
            return;
        }

        Debug.Log($"NPC command handled: {actMessage}");
    }

    private bool TryMoveTo(string destination, out string message)
    {
        if (string.IsNullOrWhiteSpace(destination))
        {
            message = "Move destination is required.";
            return false;
        }   

        Item foundItem = FindItem(destination);
        SetDestination(foundItem.transform.position);
        
        Debug.Log($"NPC move requested: actor={gameObject.name}, destination={destination}");

        message = $"{gameObject.name} moving to {destination}.";
        return true;
    }

    private bool TryFetch(string item, out string message)
    {
        if (string.IsNullOrWhiteSpace(item))
        {
            message = "Fetch item is required.";
            return false;
        }

        // TODO: Connect this placeholder to the actual NPC inventory/action system.
        Debug.Log($"NPC fetch requested: actor={gameObject.name}, item={item}");

        message = $"{gameObject.name} fetching {item}.";
        return true;
    }

    private bool HasArrived()
    {
        if (!hasActiveDestination)
            return false;

        if (navAgent.pathPending)
            return false;

        if (navAgent.remainingDistance > navAgent.stoppingDistance)
            return false;

        if (navAgent.hasPath && navAgent.velocity.sqrMagnitude > 0f)
            return false;

        return true;
    }

    private Item FindItem(string name)
    {
        Item[] items = FindObjectsByType<Item>(FindObjectsSortMode.None);
        
        foreach(Item item in items)
        {
            if(item.itemName == name)
            {
                return item;
            }
        }

        return null;
    }

    private void SetDestination(in Vector3 position)
    {
        hasActiveDestination = true;
        navAgent.SetDestination(position);    
    }

    private static string NormalizeAction(string action)
    {
        return string.IsNullOrWhiteSpace(action)
            ? string.Empty
            : action.Trim().ToLowerInvariant();
    }

    private static string FirstNonEmpty(string primary, string fallback)
    {
        return string.IsNullOrWhiteSpace(primary) ? fallback : primary;
    }

    [System.Serializable]
    private class CommandRequest
    {
        public string message;
    }

    [System.Serializable]
    private class CommandBackendResponse
    {
        public string status;
        public string input;
        public NpcCommand command;
    }

    [System.Serializable]
    public class NpcCommand
    {
        public string action;
        public string destination;
        public string item;
        public string message;
    }
}
