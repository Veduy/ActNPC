using System.Collections;
using TMPro;
using UnityEngine;

public class NPCSpeechBubble : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private TextMeshProUGUI aiMessageText;
    [SerializeField] private RectTransform bubbleRect;

    [Header("Layout")]
    [SerializeField] private Vector2 padding = new Vector2(20f, 10f);
    [SerializeField] private Vector2 minSize = new Vector2(200f, 60f);
    [SerializeField] private float fallbackMaxWidth = 1500f;

    [Header("Typing")]
    [SerializeField] private float typingSpeed = 0.035f;
    [SerializeField] private float defaultVisibleTime = 2.5f;

    private Coroutine currentRoutine;

    private void Awake()
    {
        if (bubbleRect == null)
            bubbleRect = GetComponent<RectTransform>();

        ApplyTextOverflowSettings();
        HideInstant();
    }

    public void Say(string message)
    {
        Say(message, defaultVisibleTime);
    }

    public void Say(string message, float visibleTime)
    {
        if (message == null)
            message = "";

        if (currentRoutine != null)
            StopCoroutine(currentRoutine);

        currentRoutine = StartCoroutine(SayRoutine(message, visibleTime));
    }

    private IEnumerator SayRoutine(string message, float visibleTime)
    {
        foreach (string page in SplitMessage(message))
        {
            ShowInstant();
            ResizeBubble(page);

            aiMessageText.text = "";

            foreach (char c in page)
            {
                aiMessageText.text += c;
                yield return new WaitForSeconds(typingSpeed);
            }

            yield return new WaitForSeconds(visibleTime);
        }

        HideInstant();
        currentRoutine = null;
    }

    private void ShowInstant()
    {
        canvasGroup.alpha = 1f;
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;
    }

    private void HideInstant()
    {
        canvasGroup.alpha = 0f;
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;

        if (aiMessageText != null)
            aiMessageText.text = "";
    }

    private void ResizeBubble(string message)
    {
        if (bubbleRect == null || aiMessageText == null)
            return;

        ApplyTextOverflowSettings();

        Vector2 preferredTextSize = aiMessageText.GetPreferredValues(message, 0f, 0f);

        float width = Mathf.Max(minSize.x, preferredTextSize.x + padding.x);
        float height = Mathf.Max(minSize.y, preferredTextSize.y + padding.y);

        bubbleRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
        bubbleRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
    }

    private string[] SplitMessage(string message)
    {
        if (aiMessageText == null)
            return new[] { message };

        ApplyTextOverflowSettings();

        string[] pages = SplitMessageByPunctuation(message);
        if (pages.Length > 1)
            return pages;

        return SplitMessageByPreferredWidth(message);
    }

    private string[] SplitMessageByPunctuation(string message)
    {
        var pages = new System.Collections.Generic.List<string>();
        int startIndex = 0;

        for (int i = 0; i < message.Length; i++)
        {
            if (!IsSplitPunctuation(message[i]))
                continue;

            string page = message.Substring(startIndex, i - startIndex + 1).Trim();
            if (page.Length > 0)
                pages.Add(page);

            startIndex = i + 1;
            while (startIndex < message.Length && char.IsWhiteSpace(message[startIndex]))
                startIndex++;
        }

        if (startIndex < message.Length)
        {
            string page = message.Substring(startIndex).Trim();
            if (page.Length > 0)
                pages.Add(page);
        }

        return pages.ToArray();
    }

    private string[] SplitMessageByPreferredWidth(string message)
    {
        float maxTextWidth = Mathf.Max(1f, fallbackMaxWidth - padding.x);
        if (aiMessageText.GetPreferredValues(message, 0f, 0f).x <= maxTextWidth)
            return new[] { message };

        var pages = new System.Collections.Generic.List<string>();
        int startIndex = 0;

        while (startIndex < message.Length)
        {
            int bestEndIndex = startIndex + 1;
            int low = startIndex + 1;
            int high = message.Length;

            while (low <= high)
            {
                int mid = (low + high) / 2;
                string candidate = message.Substring(startIndex, mid - startIndex).Trim();
                float width = aiMessageText.GetPreferredValues(candidate, 0f, 0f).x;

                if (width <= maxTextWidth)
                {
                    bestEndIndex = mid;
                    low = mid + 1;
                }
                else
                {
                    high = mid - 1;
                }
            }

            int splitIndex = FindWordBoundary(message, startIndex, bestEndIndex);
            string page = message.Substring(startIndex, splitIndex - startIndex).Trim();

            if (page.Length > 0)
                pages.Add(page);

            startIndex = splitIndex;
            while (startIndex < message.Length && char.IsWhiteSpace(message[startIndex]))
                startIndex++;
        }

        return pages.ToArray();
    }

    private bool IsSplitPunctuation(char c)
    {
        return c == ',' || c == '.' || c == '!' || c == '?' ||
               c == '\uFF0C' || c == '\u3002' || c == '\uFF01' || c == '\uFF1F';
    }

    private int FindWordBoundary(string message, int startIndex, int bestEndIndex)
    {
        for (int i = bestEndIndex - 1; i > startIndex; i--)
        {
            if (char.IsWhiteSpace(message[i]))
                return i;
        }

        return bestEndIndex;
    }

    private void ApplyTextOverflowSettings()
    {
        if (aiMessageText == null)
            return;

        aiMessageText.textWrappingMode = TextWrappingModes.NoWrap;
        aiMessageText.overflowMode = TextOverflowModes.Overflow;
    }
}
