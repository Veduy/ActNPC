using UnityEngine;
using UnityEngine.AI;
using System;
using System.Collections;
using System.Collections.Generic;

public class act_npc_controller : MonoBehaviour
{
    public event Action<NpcAction, string> ActionFailed;

    [SerializeField] private Rigidbody rb;

    [SerializeField] private Transform destination;
    [SerializeField] private float pickupRadius = 1.5f;
    [SerializeField] private LayerMask pickupLayers = ~0;
    [SerializeField] private NPCInventory inventory;

    private NavMeshAgent navAgent;
    private bool hasActiveDestination;
    private readonly Queue<NpcAction> actionQueue = new Queue<NpcAction>();
    private Coroutine actionQueueRoutine;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        navAgent = GetComponent<NavMeshAgent>();
        inventory = GetComponent<NPCInventory>();
        if (inventory == null)
        {
            inventory = gameObject.AddComponent<NPCInventory>();
        }
    }
    
    private void Start()
    {
        
    }

    private void Update()
    {
        if(actionQueueRoutine == null && HasArrived())
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

        if (command.actions != null)
        {
            if (command.actions.Length == 0)
            {
                message = string.IsNullOrWhiteSpace(command.message)
                    ? "No executable NPC actions were provided."
                    : command.message;
                Debug.Log($"NPC action plan was empty: actor={gameObject.name}, message={message}");
                return true;
            }

            if (ContainsStopAction(command.actions))
            {
                StopCurrentActions();
                message = $"{gameObject.name} stopped current actions.";
                return true;
            }

            int enqueuedCount = EnqueueActions(command.actions);
            if (actionQueueRoutine == null)
            {
                actionQueueRoutine = StartCoroutine(ProcessActionQueue());
            }

            message = $"{gameObject.name} enqueued {enqueuedCount} actions. Queued actions: {actionQueue.Count}.";
            return true;
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
            case "stop":
                StopCurrentActions();
                message = $"{gameObject.name} stopped current actions.";
                return true;
            case "get_item":
                return TryGetItem(FirstNonEmpty(command.@object, command.item, command.destination), out message);
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

        SceneObject target = FindSceneObject(destination, "location");
        if (target == null)
        {
            message = $"Move destination was not found: {destination}";
            return false;
        }

        SetDestination(target.transform.position);
        
        Debug.Log($"NPC move requested: actor={gameObject.name}, destination={destination}");

        message = $"{gameObject.name} moving to {destination}.";
        return true;
    }

    private int EnqueueActions(NpcAction[] actions)
    {
        int enqueuedCount = 0;

        foreach (NpcAction action in actions)
        {
            if (action == null)
            {
                continue;
            }

            actionQueue.Enqueue(action);
            enqueuedCount++;
        }

        return enqueuedCount;
    }

    private void PrependActions(params NpcAction[] actions)
    {
        Queue<NpcAction> rebuiltQueue = new Queue<NpcAction>();

        foreach (NpcAction action in actions)
        {
            if (action != null)
            {
                rebuiltQueue.Enqueue(action);
            }
        }

        while (actionQueue.Count > 0)
        {
            rebuiltQueue.Enqueue(actionQueue.Dequeue());
        }

        while (rebuiltQueue.Count > 0)
        {
            actionQueue.Enqueue(rebuiltQueue.Dequeue());
        }
    }

    private static bool ShouldRecoverGetItemByMoving(NpcAction action, string message)
    {
        return action != null
            && action.recovery_attempts <= 0
            && !string.IsNullOrWhiteSpace(action.target_id)
            && !string.IsNullOrWhiteSpace(message)
            && message.IndexOf("not within pickup range", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private IEnumerator ProcessActionQueue()
    {
        while (actionQueue.Count > 0)
        {
            NpcAction action = actionQueue.Dequeue();
            string normalizedCommand = NormalizeQueueCommand(action.command);

            switch (normalizedCommand)
            {
                case "STOP":
                    ClearMovement();
                    actionQueue.Clear();
                    actionQueueRoutine = null;
                    Debug.Log("Action queue STOP completed.");
                    yield break;

                case "MOVE_TO":
                    if (!TryStartMoveToTarget(action.target_id, out string moveMessage))
                    {
                        Debug.LogWarning($"Action queue failed: {moveMessage}");
                        NotifyActionFailed(action, moveMessage);
                        actionQueue.Clear();
                        actionQueueRoutine = null;
                        yield break;
                    }

                    Debug.Log($"Action queue: {moveMessage}");
                    yield return new WaitUntil(HasArrived);
                    hasActiveDestination = false;
                    Debug.Log($"Action queue MOVE_TO completed: target_id={action.target_id}");
                    break;

                case "GET_ITEM":
                    if (!TryGetItem(action.target_id, out string getMessage))
                    {
                        if (ShouldRecoverGetItemByMoving(action, getMessage))
                        {
                            action.recovery_attempts++;
                            PrependActions(
                                new NpcAction
                                {
                                    action_id = $"{action.action_id}_recover_move",
                                    command = "MOVE_TO",
                                    target_id = action.target_id
                                },
                                action
                            );
                            Debug.Log($"Action queue recovery inserted MOVE_TO before GET_ITEM: target_id={action.target_id}");
                            break;
                        }

                        Debug.LogWarning($"Action queue failed: {getMessage}");
                        NotifyActionFailed(action, getMessage);
                        actionQueue.Clear();
                        actionQueueRoutine = null;
                        yield break;
                    }

                    Debug.Log($"Action queue: {getMessage}");
                    break;

                default:
                    string unsupportedMessage = $"Unsupported queued command: {action.command}";
                    Debug.LogWarning($"Action queue failed: {unsupportedMessage}");
                    NotifyActionFailed(action, unsupportedMessage);
                    actionQueue.Clear();
                    actionQueueRoutine = null;
                    yield break;
            }
        }

        actionQueueRoutine = null;
        Debug.Log("NPC action queue completed.");
    }

    private void NotifyActionFailed(NpcAction action, string message)
    {
        ActionFailed?.Invoke(action, message);
    }

    private bool ContainsStopAction(NpcAction[] actions)
    {
        foreach (NpcAction action in actions)
        {
            if (action != null && NormalizeQueueCommand(action.command) == "STOP")
            {
                return true;
            }
        }

        return false;
    }

    private void StopCurrentActions()
    {
        actionQueue.Clear();

        if (actionQueueRoutine != null)
        {
            StopCoroutine(actionQueueRoutine);
            actionQueueRoutine = null;
        }

        ClearMovement();
        Debug.Log("NPC current actions stopped.");
    }

    private void ClearMovement()
    {
        hasActiveDestination = false;

        if (navAgent != null)
        {
            navAgent.ResetPath();
            navAgent.isStopped = true;
            navAgent.velocity = Vector3.zero;
        }
    }

    private bool TryStartMoveToTarget(string targetId, out string message)
    {
        if (string.IsNullOrWhiteSpace(targetId))
        {
            message = "MOVE_TO target_id is required.";
            return false;
        }

        SceneObject target = FindSceneObject(targetId, "location");
        if (target == null)
        {
            message = $"MOVE_TO target was not found: {targetId}";
            return false;
        }

        SetDestination(target.transform.position);
        message = $"{gameObject.name} moving to {targetId}.";
        return true;
    }

    private bool TryGetItem(string targetId, out string message)
    {
        if (string.IsNullOrWhiteSpace(targetId))
        {
            message = "GET_ITEM target_id is required.";
            return false;
        }

        SceneObject target = FindSceneObject(targetId, "item");
        Item targetItem = target == null ? FindItem(targetId) : FindItemComponent(target);
        if (targetItem == null)
        {
            message = $"GET_ITEM target was not found: {targetId}";
            return false;
        }

        if (!IsItemInPickupRange(targetItem))
        {
            message = $"GET_ITEM target is not within pickup range: {targetId}";
            return false;
        }

        NPCInventory.InventoryItem inventoryItem = inventory.AddItem(targetItem, targetId);
        if (inventoryItem == null)
        {
            message = $"GET_ITEM target could not be added to inventory: {targetId}";
            return false;
        }

        targetItem.gameObject.SetActive(false);
        Destroy(targetItem.gameObject);
        message = $"{gameObject.name} got item {inventoryItem.itemName}. Item count: {inventoryItem.count}.";
        return true;
    }

    private bool IsItemInPickupRange(Item targetItem)
    {
        if (targetItem == null)
        {
            return false;
        }

        Collider[] colliders = Physics.OverlapSphere(
            transform.position,
            pickupRadius,
            pickupLayers,
            QueryTriggerInteraction.Collide
        );

        Transform targetTransform = targetItem.transform;
        foreach (Collider detectedCollider in colliders)
        {
            if (detectedCollider == null)
            {
                continue;
            }

            Transform detectedTransform = detectedCollider.transform;
            if (detectedTransform == targetTransform
                || detectedTransform.IsChildOf(targetTransform)
                || targetTransform.IsChildOf(detectedTransform))
            {
                return true;
            }
        }

        return false;
    }

    private static Item FindItemComponent(SceneObject sceneObject)
    {
        if (sceneObject == null)
        {
            return null;
        }

        Item item = sceneObject.GetComponent<Item>();
        if (item != null)
        {
            return item;
        }

        item = sceneObject.GetComponentInChildren<Item>();
        if (item != null)
        {
            return item;
        }

        return sceneObject.GetComponentInParent<Item>();
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
            case "get_inventory":
                result = InventoryResult();
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
        string objectType = args == null ? null : args.object_type;
        List<ClientObjectInfo> matches = new List<ClientObjectInfo>();
        List<SceneObject> sceneObjects = SceneObjectRegistry.SearchClosest(query, objectType, maxResults, transform.position);

        foreach (SceneObject candidate in sceneObjects)
        {
            matches.Add(CreateObjectInfo(candidate, query));
        }

        if (matches.Count == 0)
        {
            AddLegacyItemMatches(query, maxResults, matches);
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
                state = actionQueueRoutine != null ? "busy" : "idle"
            }
        };
    }

    private ClientFunctionResult InventoryResult()
    {
        return new ClientFunctionResult
        {
            ok = true,
            inventory = inventory == null ? new NPCInventory.InventoryItem[0] : inventory.Snapshot()
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
        SceneObject sceneObject = FindSceneObject(name, "item");
        if (sceneObject != null)
        {
            Item sceneItem = sceneObject.GetComponent<Item>();
            if (sceneItem != null)
            {
                return sceneItem;
            }
        }

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

    private SceneObject FindSceneObject(string queryOrId, string objectType)
    {
        return SceneObjectRegistry.FindClosest(queryOrId, objectType, transform.position);
    }

    private static void AddLegacyItemMatches(string query, int maxResults, List<ClientObjectInfo> matches)
    {
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
            || string.Equals(item.itemId.ToString(), normalizedQuery, StringComparison.OrdinalIgnoreCase)
            || itemName.IndexOf(normalizedQuery, StringComparison.OrdinalIgnoreCase) >= 0
            || objectName.IndexOf(normalizedQuery, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static ClientObjectInfo CreateObjectInfo(Item item, string query)
    {
        return CreateObjectInfo(item, query, true);
    }

    private static ClientObjectInfo CreateObjectInfo(Item item, string query, bool reachable)
    {
        return new ClientObjectInfo
        {
            object_id = item.itemId.ToString(),
            name = item.itemName,
            type = "item",
            position = item.transform.position,
            status = item.gameObject.activeInHierarchy ? "available" : "disabled",
            reachable = reachable,
            confidence = string.Equals(item.itemName, query, StringComparison.OrdinalIgnoreCase) ? 1f : 0.75f
        };
    }

    private static ClientObjectInfo CreateObjectInfo(SceneObject sceneObject, string query)
    {
        return CreateObjectInfo(sceneObject, query, true);
    }

    private static ClientObjectInfo CreateObjectInfo(SceneObject sceneObject, string query, bool reachable)
    {
        return new ClientObjectInfo
        {
            object_id = sceneObject.ResolvedObjectId,
            name = sceneObject.ResolvedDisplayName,
            type = sceneObject.objectType.ToString().ToLowerInvariant(),
            position = sceneObject.transform.position,
            status = sceneObject.gameObject.activeInHierarchy ? "available" : "disabled",
            reachable = reachable,
            confidence = sceneObject.GetMatchConfidence(query)
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
        navAgent.isStopped = false;
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

    private static string NormalizeQueueCommand(string command)
    {
        return string.IsNullOrWhiteSpace(command)
            ? string.Empty
            : command.Trim().ToUpperInvariant();
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
        public NpcAction[] actions;
    }

    [System.Serializable]
    public class NpcAction
    {
        public string action_id;
        public string command;
        public string target_id;
        public int recovery_attempts;
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
        public NPCInventory.InventoryItem[] inventory;
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
