using System.Collections;
using TMPro;
using UnityEngine;

public class NPCSpeechBubble : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private TextMeshProUGUI aiMessageText;

    [Header("Typing")]
    [SerializeField] private float typingSpeed = 0.035f;

    private Coroutine currentRoutine;

    private void Awake()
    {
        HideInstant();
    }

    public void Say(string message, float visibleTime = 2.5f)
    {
        if (currentRoutine != null)
            StopCoroutine(currentRoutine);

        currentRoutine = StartCoroutine(SayRoutine(message, visibleTime));
    }

    private IEnumerator SayRoutine(string message, float visibleTime)
    {
        ShowInstant();

        aiMessageText.text = "";

        foreach (char c in message)
        {
            aiMessageText.text += c;
            yield return new WaitForSeconds(typingSpeed);
        }

        yield return new WaitForSeconds(visibleTime);

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
}