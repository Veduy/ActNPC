using System;
using UnityEngine;

public enum SceneObjectType
{
    Item,
    Location,
    Agent,
    User,
    Interaction
}

public class SceneObject : MonoBehaviour
{
    public string objectId;
    public string displayName;
    public string[] aliases;
    public SceneObjectType objectType = SceneObjectType.Location;

    public string ResolvedObjectId
    {
        get
        {
            return string.IsNullOrWhiteSpace(objectId)
                ? gameObject.name
                : objectId.Trim();
        }
    }

    public string ResolvedDisplayName
    {
        get
        {
            return string.IsNullOrWhiteSpace(displayName)
                ? gameObject.name
                : displayName.Trim();
        }
    }

    public bool MatchesQuery(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return false;
        }

        string normalizedQuery = NormalizeSearchQuery(query);

        return MatchesText(ResolvedObjectId, normalizedQuery)
            || MatchesText(ResolvedDisplayName, normalizedQuery)
            || MatchesText(gameObject.name, normalizedQuery)
            || MatchesAliases(normalizedQuery);
    }

    public bool MatchesObjectType(string requestedType)
    {
        if (string.IsNullOrWhiteSpace(requestedType))
        {
            return true;
        }

        string normalizedType = requestedType.Trim().ToLowerInvariant();
        switch (normalizedType)
        {
            case "item":
                return objectType == SceneObjectType.Item;
            case "agent":
            case "npc":
                return objectType == SceneObjectType.Agent;
            case "user":
            case "player":
                return objectType == SceneObjectType.User;
            case "interaction":
            case "interactable":
                return objectType == SceneObjectType.Interaction;
            case "location":
            case "place":
            case "position":
                return true;
            default:
                return true;
        }
    }

    public float GetMatchConfidence(string query)
    {
        string normalizedQuery = NormalizeSearchQuery(query);

        if (string.Equals(NormalizeSearchQuery(ResolvedObjectId), normalizedQuery, StringComparison.OrdinalIgnoreCase)
            || string.Equals(NormalizeSearchQuery(ResolvedDisplayName), normalizedQuery, StringComparison.OrdinalIgnoreCase)
            || AliasEquals(normalizedQuery))
        {
            return 1f;
        }

        return 0.75f;
    }

    public static string NormalizeSearchQuery(string query)
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

    private bool MatchesAliases(string normalizedQuery)
    {
        if (aliases == null)
        {
            return false;
        }

        foreach (string alias in aliases)
        {
            if (MatchesText(alias, normalizedQuery))
            {
                return true;
            }
        }

        return false;
    }

    private bool AliasEquals(string normalizedQuery)
    {
        if (aliases == null)
        {
            return false;
        }

        foreach (string alias in aliases)
        {
            if (string.Equals(NormalizeSearchQuery(alias), normalizedQuery, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesText(string value, string normalizedQuery)
    {
        string normalizedValue = NormalizeSearchQuery(value);
        return string.Equals(normalizedValue, normalizedQuery, StringComparison.OrdinalIgnoreCase)
            || normalizedValue.IndexOf(normalizedQuery, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
