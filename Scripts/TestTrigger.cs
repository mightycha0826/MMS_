using System.Collections;
using UnityEngine;

public class TestTrigger : MonoBehaviour
{
    public Helper helper;
    [SerializeField] private GeminiClient gc;
    private void Awake()
    {
        helper = Helper.Instance;
    }

    void Start()
    {
        StartCoroutine(MMSIntro());
    }

    IEnumerator MMSIntro()
    {
        while (!Input.GetKeyDown(KeyCode.RightArrow)) yield return null;
        Helper.Instance.PlayResponse("안녕하세요, 교사 구상효입니다.", "smile");
        yield return null;
        while (!Input.GetKeyDown(KeyCode.RightArrow)) yield return null;
        Helper.Instance.PlayResponse("흥미로운 발표 잘 들었습니다.", "smile");
        yield return null;
        while (!Input.GetKeyDown(KeyCode.RightArrow)) yield return null;
        Helper.Instance.PlayResponse("몇 가지 질문을 드리고자 합니다.", "neutral");
        yield return null;

        gc.SendSessionStart();
    }

    void StartMessage()
    {
        
    }

    void Update()
    {
        switch (Input.inputString)
        {
            case "1":
                Helper.Instance.PlayResponse("자기소개를 부탁드립니다.", "neutral");
                break;
            case "2":
                Helper.Instance.PlayResponse("좋은 답변이네요!", "smile");
                break;
            case "3":
                Helper.Instance.PlayResponse("아, 제가 좀 당황스러운 질문을 드렸나요?", "shy");
                break;
            case "4":
                Helper.Instance.PlayResponse("그 부분에 대해 좀 더 자세히 설명해주세요.", "serious");
                break;
            case "5":
                Helper.Instance.PlayResponse("음… 그게 무슨 뜻인가요?", "confused");
                break;
            case "6":
                Helper.Instance.PlayResponse("정말 그게 본인의 솔직한 답변입니까?", "pressuring");
                break;
            case "7":
                Helper.Instance.PlayResponse("훌륭한 답변이었습니다.", "satisfied");
                break;
        }
        
        if (Input.GetKeyDown(KeyCode.V))
        {
            var wc = GetComponent<MicDisplayController>();
            if (wc.GetState() == MicDisplayController.WaveState.Done)
            {
                helper.SetMicState(MicDisplayController.WaveState.Idle);
                helper.ShowMicOverlay();
            }
            else if (wc.GetState() == MicDisplayController.WaveState.Listening)
            {
                helper.StopListening();
            }
            else
            {
                helper.StartListening();
            }
        }
    }
}