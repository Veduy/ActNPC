using UnityEngine;
using UnityEngine.AI;
using System;
using System.Collections.Generic;

public class act_npc_controller : MonoBehaviour
{
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
                return TryFetch(FirstNonEmpty(command.@object, command.item, command.destination), out message);
            case "move":
                return TryMoveTo(FirstNonEmpty(command.@object, command.destination, command.item), out message);
            default:
                message = $"Unsupported NPC action: {command.action}";
                return false;
        }
    }

    private bool TryMoveTo(string destination, out string message)
    {
        if (string.IsNullOrWhiteSpace(destination))
        {
            message = "Move destination is required.";
            return false;
        }

        Item foundItem = FindItem(destination);
        if (foundItem == null)
        {
            message = $"Move destination was not found: {destination}";
            return false;
        }

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

    public bool TryHandleClientFunction(string functionName, ClientFunctionArgs args, out ClientFunctionResult result)
    {
        string normalizedFunction = string.IsNullOrWhiteSpace(functionName)
            ? string.Empty
            : functionName.Trim().ToLowerInvariant();

        switch (normalizedFunction)
        {
            case "find_object":
                result = FindObjectResult(args);
                return result.ok;
            case "get_agent_state":
                result = AgentStateResult();
                return true;
            default:
                result = ErrorResult("FUNCTION_NOT_ALLOWED", $"Unsupported client function: {functionName}");
                return false;
        }
    }

    private ClientFunctionResult FindObjectResult(ClientFunctionArgs args)
    {
        string query = args == null ? null : args.query;
        if (string.IsNullOrWhiteSpace(query))
        {
            return ErrorResult("QUERY_REQUIRED", "find_object requires args.query.");
        }

        int maxResults = args != null && args.max_results > 0 ? args.max_results : 5;
        List<ClientObjectInfo> matches = new List<ClientObjectInfo>();
        Item[] items = FindObjectsByType<Item>(FindObjectsSortMode.None);

        foreach (Item candidate in items)
        {
            if (!ItemMatches(candidate, query))
            {
                continue;
            }

            matches.Add(CreateObjectInfo(candidate, query));
            if (matches.Count >= maxResults)
            {
                break;
            }
        }

        ClientFunctionResult result = new ClientFunctionResult
        {
            ok = true,
            objects = matches.ToArray()
        };

        return result;
    }

    private ClientFunctionResult AgentStateResult()
    {
        return new ClientFunctionResult
        {
            ok = true,
            agent = new AgentState
            {
                agent_id = gameObject.name,
                position = transform.position,
                state = hasActiveDestination ? "moving" : "idle"
            }
        };
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
            if(ItemMatches(item, name))
            {
                return item;
            }
        }

        return null;
    }

    private static bool ItemMatches(Item item, string query)
    {
        if (item == null || string.IsNullOrWhiteSpace(query))
        {
            return false;
        }

        string normalizedQuery = NormalizeSearchQuery(query);
        string itemName = NormalizeSearchQuery(item.itemName);
        string objectName = NormalizeSearchQuery(item.gameObject.name);

        return string.Equals(itemName, normalizedQuery, StringComparison.OrdinalIgnoreCase)
            || string.Equals(objectName, normalizedQuery, StringComparison.OrdinalIgnoreCase)
            || itemName.IndexOf(normalizedQuery, StringComparison.OrdinalIgnoreCase) >= 0
            || objectName.IndexOf(normalizedQuery, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static ClientObjectInfo CreateObjectInfo(Item item, string query)
    {
        return new ClientObjectInfo
        {
            object_id = item.gameObject.name,
            name = item.itemName,
            type = "item",
            position = item.transform.position,
            status = item.gameObject.activeInHierarchy ? "available" : "disabled",
            reachable = true,
            confidence = string.Equals(item.itemName, query, StringComparison.OrdinalIgnoreCase) ? 1f : 0.75f
        };
    }

    private static ClientFunctionResult ErrorResult(string code, string message)
    {
        return new ClientFunctionResult
        {
            ok = false,
            error = new ClientFunctionError
            {
                code = code,
                message = message
            }
        };
    }

    private static string NormalizeSearchQuery(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return string.Empty;
        }

        string normalized = query.Trim().ToLowerInvariant();
        string[] genericSuffixes =
        {
            " location",
            " position",
            " place",
            " spot",
            " area",
            " nearby",
            " near",
            " around"
        };

        foreach (string genericSuffix in genericSuffixes)
        {
            normalized = normalized.Replace(genericSuffix, string.Empty);
        }

        string[] genericPrefixes =
        {
            "location of ",
            "position of ",
            "place of ",
            "near ",
            "around "
        };

        foreach (string genericPrefix in genericPrefixes)
        {
            if (normalized.StartsWith(genericPrefix, StringComparison.Ordinal))
            {
                normalized = normalized.Substring(genericPrefix.Length);
            }
        }

        return normalized.Trim();
    }

    private void SetDestination(in Vector3 position)
    {
        hasActiveDestination = true;
        navAgent.SetDestination(position);    
    }

    private static string NormalizeAction(string action)
    {
        if (string.IsNullOrWhiteSpace(action))
        {
            return string.Empty;
        }

        string normalizedAction = action.Trim().ToLowerInvariant();
        if (normalizedAction == "null" || normalizedAction == "none" || normalizedAction == "no_action")
        {
            return string.Empty;
        }

        return normalizedAction;
    }

    private static string FirstNonEmpty(params string[] values)
    {
        foreach (string value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    [System.Serializable]
    public class NpcCommand
    {
        public string action;
        public string destination;
        public string item;
        public string @object;
        public string message;
    }

    [Serializable]
    public class ClientFunctionArgs
    {
        public string query;
        public string object_type;
        public string object_id;
        public int max_results;
    }

    [Serializable]
    public class ClientFunctionResult
    {
        public bool ok;
        public ClientObjectInfo[] objects;
        public AgentState agent;
        public ClientFunctionError error;
    }

    [Serializable]
    public class ClientObjectInfo
    {
        public string object_id;
        public string name;
        public string type;
        public Vector3 position;
        public string status;
        public bool reachable;
        public float confidence;
    }

    [Serializable]
    public class AgentState
    {
        public string agent_id;
        public Vector3 position;
        public string state;
    }

    [Serializable]
    public class ClientFunctionError
    {
        public string code;
        public string message;
    }
}