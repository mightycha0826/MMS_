using UnityEngine;

public class TestTrigger : MonoBehaviour
{
    public InterviewerController interviewer;
    public SubtitleManager subtitle;

    int index = 0;
    void Update()
    {
        // 1키
        if (Input.GetKeyDown(KeyCode.Alpha1))
        {
            index++;
            if (index > 6) index = 0;
            interviewer.SetMood((InterviewerController.InterviewerMood)index);
            interviewer.StopSpeaking();
            interviewer.StartSpeaking();
            subtitle.DisplaySubtitle("안녕하세요! 저는 구상효예요!");
        }

        // 2키
        if (Input.GetKeyDown(KeyCode.Alpha2))
        {
            index--;
            if (index < 0) index = 6;
            interviewer.SetMood((InterviewerController.InterviewerMood)index);
            interviewer.StopSpeaking();
            interviewer.StartSpeaking();
            subtitle.DisplaySubtitle("안녕하세요, 오늘 이렇게 면접 자리에 나와 주셔서 진심으로 감사드립니다. 저도 여러분과 같은 나이에 비슷한 고민을 했던 기억이 납니다. 진로를 결정한다는 것이 쉬운 일이 아니라는 걸 저도 잘 알고 있어요. 그래서 오늘은 정답을 찾으려 하기보다는, 여러분이 평소에 어떤 생각을 하고 있는지, 어떤 것에 관심을 가지고 있는지를 함께 이야기해 보는 시간으로 생각해 주셨으면 합니다. 자, 그럼 시작해 볼게요. 먼저 자기소개를 부탁드립니다. 이름과 학년, 그리고 요즘 가장 관심 있는 것이 무엇인지 편하게 말씀해 주세요. 그리고 가능하다면 그 관심사가 생기게 된 계기도 함께 이야기해 주시면 더욱 좋겠습니다. 긴장하지 않아도 됩니다. 여기서 하는 말은 옳고 그름이 없으니까요.");
        }
        
        if (Input.GetKeyDown(KeyCode.V))
        {
            var wc = GetComponent<MicDisplayController>();
            if (wc.GetState() == MicDisplayController.WaveState.Idle)
            {
                wc.SetState(MicDisplayController.WaveState.Listening);
            }
            else if (wc.GetState() == MicDisplayController.WaveState.Listening)
            {
                wc.HideDisplay();
                wc.SetState(MicDisplayController.WaveState.Done);
            }
            else
            {
                wc.ShowDisplay();
                wc.SetState(MicDisplayController.WaveState.Idle);
            }
        }
        // Space 바
        if (Input.GetKeyDown(KeyCode.Space))
        {
            if (interviewer.IsSpeaking())
            {
                interviewer.StopSpeaking();
            }
            else
            {
                interviewer.StartSpeaking();
            }
        }
    }
}