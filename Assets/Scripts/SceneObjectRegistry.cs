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

        int resultLimit = maxResults > 0 ? maxResults : int.MaxValue;
        SceneObject[] objects = FindSceneObjects();

        foreach (SceneObject candidate in objects)
        {
            if (candidate == null
                || !candidate.MatchesObjectType(objectType)
                || !candidate.MatchesQuery(query))
            {
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

    public static List<SceneObject> SearchClosest(string query, string objectType, int maxResults, Vector3 origin)
    {
        List<SceneObject> matches = Search(query, objectType, 0);
        matches.Sort((left, right) =>
            Vector3.SqrMagnitude(left.transform.position - origin)
                .CompareTo(Vector3.SqrMagnitude(right.transform.position - origin)));

        int resultLimit = maxResults > 0 ? maxResults : 5;
        if (matches.Count > resultLimit)
        {
            matches.RemoveRange(resultLimit, matches.Count - resultLimit);
        }

        return matches;
    }

    public static SceneObject FindFirst(string query, string objectType)
    {
        List<SceneObject> matches = Search(query, objectType, 1);
        return matches.Count > 0 ? matches[0] : null;
    }

    public static SceneObject FindClosest(string query, string objectType, Vector3 origin)
    {
        List<SceneObject> matches = SearchClosest(query, objectType, 1, origin);
        return matches.Count > 0 ? matches[0] : null;
    }

    private static SceneObject[] FindSceneObjects()
    {
        return UnityEngine.Object.FindObjectsByType<SceneObject>(FindObjectsSortMode.None);
    }
}
