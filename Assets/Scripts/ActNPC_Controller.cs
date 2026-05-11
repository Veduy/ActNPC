using UnityEngine;
using UnityEngine.AI;
using System.Collections;
using System.Collections.Generic;

public class act_npc_controller : MonoBehaviour
{
    [SerializeField] private Rigidbody rb;

    [SerializeField] private float pickupRadius = 1.5f;
    [SerializeField] private float putItemDistance = 1.25f;
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
            ClearMovement();
        }
    }


    public bool TryAct(NpcCommand command, out string message)
    {
        if (command == null)
        {
            message = "NPC command is required.";
            return false;
        }

        if (command.actions == null || command.actions.Length == 0)
        {
            message = string.IsNullOrWhiteSpace(command.message)
                ? "No executable NPC actions were provided."
                : command.message;
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

    private IEnumerator ProcessActionQueue()
    {
        while (actionQueue.Count > 0)
        {
            NpcAction action = actionQueue.Dequeue();

            switch (action == null ? null : action.command)
            {
                case "STOP":
                    ClearMovement();
                    actionQueue.Clear();
                    actionQueueRoutine = null;
                    yield break;

                case "MOVE_TO":
                    if (!TryStartMoveToTarget(action, out string moveMessage))
                    {
                        Debug.LogWarning($"Action queue failed: {moveMessage}");
                        actionQueue.Clear();
                        actionQueueRoutine = null;
                        yield break;
                    }

                    yield return new WaitUntil(HasArrived);
                    ClearMovement();
                    break;

                case "GET_ITEM":
                    if (!TryGetItem(GetActionTarget(action), out string getMessage))
                    {
                        Debug.LogWarning($"Action queue failed: {getMessage}");
                        actionQueue.Clear();
                        actionQueueRoutine = null;
                        yield break;
                    }

                    break;

                case "PUT_ITEM":
                    if (!TryPutItem(GetActionTarget(action), out string putMessage))
                    {
                        Debug.LogWarning($"Action queue failed: {putMessage}");
                        actionQueue.Clear();
                        actionQueueRoutine = null;
                        yield break;
                    }

                    break;

                default:
                    string unsupportedMessage = $"Unsupported queued command: {(action == null ? "null" : action.command)}";
                    Debug.LogWarning($"Action queue failed: {unsupportedMessage}");
                    actionQueue.Clear();
                    actionQueueRoutine = null;
                    yield break;
            }
        }

        actionQueueRoutine = null;
    }

    private bool ContainsStopAction(NpcAction[] actions)
    {
        foreach (NpcAction action in actions)
        {
            if (action != null && action.command == "STOP")
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

    private bool TryStartMoveToTarget(NpcAction action, out string message)
    {
        string targetId = GetActionTarget(action);
        if (string.IsNullOrWhiteSpace(targetId))
        {
            if (action != null && action.position != Vector3.zero)
            {
                SetDestination(action.position);
                message = $"{gameObject.name} moving to position {action.position}.";
                return true;
            }

            message = "MOVE_TO target is required.";
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
            message = "GET_ITEM target is required.";
            return false;
        }

        SceneObject target = FindSceneObject(targetId, "item");
        if (target == null)
        {
            message = $"GET_ITEM target was not found: {targetId}";
            return false;
        }

        Item targetItem = FindItemComponent(target);
        if (targetItem == null)
        {
            message = $"GET_ITEM target has no item component: {targetId}";
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
        message = $"{gameObject.name} got item {inventoryItem.itemName}. Item count: {inventoryItem.count}.";
        return true;
    }

    private bool TryPutItem(string targetId, out string message)
    {
        if (string.IsNullOrWhiteSpace(targetId))
        {
            message = "PUT_ITEM target is required.";
            return false;
        }

        if (inventory == null || !inventory.TryTakeItem(targetId, out Item item))
        {
            message = $"PUT_ITEM inventory target was not found: {targetId}";
            return false;
        }

        PlaceInventoryItem(item);
        message = $"{gameObject.name} put down item {item.itemName}.";
        return true;
    }

    private void PlaceInventoryItem(Item item)
    {
        if (item == null)
        {
            return;
        }

        Vector3 forward = transform.forward.sqrMagnitude > 0.001f ? transform.forward.normalized : Vector3.forward;
        Vector3 spawnPosition = transform.position + forward * putItemDistance;
        item.transform.position = spawnPosition;
        item.gameObject.SetActive(true);
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
        switch (functionName)
        {
            case "find_scene_objects":
                result = FindSceneObjectsResult(args);
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

    private ClientFunctionResult FindSceneObjectsResult(ClientFunctionArgs args)
    {
        string query = args == null ? null : args.query;
        if (string.IsNullOrWhiteSpace(query))
        {
            return ErrorResult("QUERY_REQUIRED", "find_scene_objects requires args.query.");
        }

        int maxResults = args != null && args.max_results > 0 ? args.max_results : 5;
        string objectType = args == null ? null : args.object_type;
        List<SceneObject> sceneObjects = SceneObjectRegistry.SearchClosest(query, objectType, maxResults, transform.position);
        List<ClientObjectInfo> objects = new List<ClientObjectInfo>();

        foreach (SceneObject sceneObject in sceneObjects)
        {
            if (sceneObject == null)
            {
                continue;
            }

            objects.Add(CreateObjectInfo(sceneObject));
        }

        return new ClientFunctionResult
        {
            ok = true,
            objects = objects.ToArray()
        };
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
                state = actionQueueRoutine == null ? "idle" : "busy",
                pickup_radius = pickupRadius
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

    private ClientObjectInfo CreateObjectInfo(SceneObject sceneObject)
    {
        return new ClientObjectInfo
        {
            object_id = sceneObject.ResolvedObjectId,
            object_name = sceneObject.ResolvedDisplayName,
            type = sceneObject.objectType.ToString().ToLowerInvariant(),
            position = sceneObject.transform.position,
            active = sceneObject.gameObject.activeInHierarchy,
            distance = Vector3.Distance(transform.position, sceneObject.transform.position)
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

    private SceneObject FindSceneObject(string queryOrId, string objectType)
    {
        return SceneObjectRegistry.FindClosest(queryOrId, objectType, transform.position);
    }

    private void SetDestination(in Vector3 position)
    {
        hasActiveDestination = true;
        navAgent.isStopped = false;
        navAgent.SetDestination(position);    
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

    private static string GetActionTarget(NpcAction action)
    {
        return action == null ? null : FirstNonEmpty(action.object_id, action.object_name);
    }

    [System.Serializable]
    public class NpcCommand
    {
        public string message;
        public NpcAction[] actions;
    }

    [System.Serializable]
    public class NpcAction
    {
        public string action_id;
        public string command;
        public string object_name;
        public string object_id;
        public Vector3 position;
    }

    [System.Serializable]
    public class ClientFunctionArgs
    {
        public string query;
        public string object_type;
        public int max_results;
    }

    [System.Serializable]
    public class ClientFunctionResult
    {
        public bool ok;
        public ClientObjectInfo[] objects;
        public AgentState agent;
        public NPCInventory.InventoryItem[] inventory;
        public ClientFunctionError error;
    }

    [System.Serializable]
    public class ClientObjectInfo
    {
        public string object_id;
        public string object_name;
        public string type;
        public Vector3 position;
        public bool active;
        public float distance;
    }

    [System.Serializable]
    public class AgentState
    {
        public string agent_id;
        public Vector3 position;
        public string state;
        public float pickup_radius;
    }

    [System.Serializable]
    public class ClientFunctionError
    {
        public string code;
        public string message;
    }
}
