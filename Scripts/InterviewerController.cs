using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class InterviewerController : MonoBehaviour
{
    [System.Serializable]
    public class MoodSet
    {
        public InterviewerMood mood;
        public Texture2D idleImage;
        public Texture2D speakingImage;
    }

    public enum InterviewerMood
    {
        Neutral,        // 중립
        Smile,          // 미소
        Shy,            // 부끄러움
        Serious,        // 진지함
        Confused,       // 당황
        Pressuring,     // 압박
        Satisfied       // 만족
    }

    [SerializeField] private RawImage characterImageDisplay;
    [SerializeField] private List<MoodSet> moodImages = new List<MoodSet>();

    private InterviewerMood currentMood = InterviewerMood.Neutral;
    private bool isCurrentlySpeaking = false;
    private CanvasGroup imageCanvasGroup;

    [SerializeField] private float mouthOpenSpeed = 0.1f;
    private Coroutine mouthRoutine;

    [Header("Breathing Effect")]
    [SerializeField] private bool useBreathing = true;
    [SerializeField] private float breatheSpeed = 2.0f;
    [SerializeField] private float breatheAmount = 0.02f;
    private Vector3 initialScale;


    private static readonly Dictionary<InterviewerMood, string> MoodLabels
        = new Dictionary<InterviewerMood, string>
    {
    { InterviewerMood.Neutral,    "중립"     },
    { InterviewerMood.Smile,      "미소"     },
    { InterviewerMood.Shy,        "부끄러움" },
    { InterviewerMood.Serious,    "진지함"   },
    { InterviewerMood.Confused,   "당황"     },
    { InterviewerMood.Pressuring, "압박"     },
    { InterviewerMood.Satisfied,  "만족"     },
    };

    private void Start()
    {
        if (characterImageDisplay == null)
        {
            characterImageDisplay = GetComponent<RawImage>();
        }

        imageCanvasGroup = characterImageDisplay.GetComponent<CanvasGroup>();
        if (imageCanvasGroup == null)
        {
            imageCanvasGroup = characterImageDisplay.gameObject.AddComponent<CanvasGroup>();
        }

        SetMood(InterviewerMood.Neutral);

        initialScale = characterImageDisplay.transform.localScale;

        if (useBreathing)
        {
            StartCoroutine(BreathingLoop());
        }

        SetMood(InterviewerMood.Neutral);
    }

    private IEnumerator BreathingLoop()
    {
        while (true)
        {
            float scaleOffset = Mathf.Sin(Time.time * breatheSpeed) * breatheAmount;

            characterImageDisplay.transform.localScale = new Vector3(
                initialScale.x - (scaleOffset * 0.5f),
                initialScale.y + scaleOffset,
                initialScale.z
            );

            yield return null;
        }
    }
    public void SetMood(string moodString)
    {
        if (System.Enum.TryParse<InterviewerMood>(moodString, true, out var mood))
        {
            SetMood(mood);
        }
        else
        {
            Debug.LogWarning($"{moodString} < ??");
            SetMood(InterviewerMood.Neutral);
        }
    }

    public void SetMood(InterviewerMood mood)
    {
        currentMood = mood;
        UpdateDisplayImage();
    }
    public bool IsSpeaking()
    {
        return isCurrentlySpeaking;
    }
    public void StartSpeaking()
    {
        if (isCurrentlySpeaking) return;
        isCurrentlySpeaking = true;

        if (mouthRoutine != null) StopCoroutine(mouthRoutine);
        mouthRoutine = StartCoroutine(MouthAnimationLoop());
    }

    public void StopSpeaking()
    {
        isCurrentlySpeaking = false;
        if (mouthRoutine != null)
        {
            StopCoroutine(mouthRoutine);
            mouthRoutine = null;
        }
        UpdateDisplayImage();
    }

    private IEnumerator MouthAnimationLoop()
    {
        var moodSet = GetMoodSet(currentMood);
        if (moodSet == null) yield break;

        while (isCurrentlySpeaking)
        {
            characterImageDisplay.texture = moodSet.speakingImage;
            yield return new WaitForSeconds(mouthOpenSpeed);
            characterImageDisplay.texture = moodSet.idleImage;
            yield return new WaitForSeconds(mouthOpenSpeed);
        }
    }
    private void UpdateDisplayImage()
    {
        var moodSet = GetMoodSet(currentMood);
        if (moodSet != null)
        {
            characterImageDisplay.texture = isCurrentlySpeaking ? moodSet.speakingImage : moodSet.idleImage;
        }
    }

    private MoodSet GetMoodSet(InterviewerMood mood)
    {
        foreach (var set in moodImages)
        {
            if (set.mood == mood)
                return set;
        }
        return null;
    }
}