using UnityEngine;
using UnityEngine.AI;

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
                return TryFetch(FirstNonEmpty(command.item, command.destination), out message);
            case "move":
                return TryMoveTo(FirstNonEmpty(command.destination, command.item), out message);
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
    public class NpcCommand
    {
        public string action;
        public string destination;
        public string item;
        public string message;
    }
}
