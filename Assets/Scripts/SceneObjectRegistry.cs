using System;
using System.Collections.Generic;
using UnityEngine;

public static class SceneObjectRegistry
{
    public static bool TryGet(string objectId, out SceneObject sceneObject)
    {
        sceneObject = null;

        if (string.IsNullOrWhiteSpace(objectId))
        {
            return false;
        }

        string normalizedId = SceneObject.NormalizeSearchQuery(objectId);
        SceneObject[] objects = FindSceneObjects();

        foreach (SceneObject candidate in objects)
        {
            if (candidate == null)
            {
                continue;
            }

            if (string.Equals(
                SceneObject.NormalizeSearchQuery(candidate.ResolvedObjectId),
                normalizedId,
                StringComparison.OrdinalIgnoreCase))
            {
                sceneObject = candidate;
                return true;
            }
        }

        return false;
    }

    public static List<SceneObject> Search(string query, string objectType, int maxResults)
    {
        List<SceneObject> matches = new List<SceneObject>();

        if (string.IsNullOrWhiteSpace(query))
        {
            return matches;
        }

        int resultLimit = maxResults > 0 ? maxResults : 5;
        SceneObject[] objects = FindSceneObjects();
        HashSet<string> seenObjectIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (SceneObject candidate in objects)
        {
            if (candidate == null
                || !candidate.MatchesObjectType(objectType)
                || !candidate.MatchesQuery(query))
            {
                continue;
            }

            string objectId = candidate.ResolvedObjectId;
            if (!seenObjectIds.Add(objectId))
            {
                Debug.LogWarning($"Duplicate SceneObject objectId detected: {objectId}");
                continue;
            }

            matches.Add(candidate);
            if (matches.Count >= resultLimit)
            {
                break;
            }
        }

        return matches;
    }

    public static SceneObject FindFirst(string query, string objectType)
    {
        List<SceneObject> matches = Search(query, objectType, 1);
        return matches.Count > 0 ? matches[0] : null;
    }

    private static SceneObject[] FindSceneObjects()
    {
        return UnityEngine.Object.FindObjectsByType<SceneObject>(FindObjectsSortMode.None);
    }
}
