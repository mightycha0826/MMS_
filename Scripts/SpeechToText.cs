using UnityEngine;
using System.Threading.Tasks;
using Whisper;
using Whisper.Utils;

public class KoreanSpeechManager : MonoBehaviour
{
    [SerializeField] private WhisperManager whisperManager;
    [SerializeField] private MicrophoneRecord microphoneRecord;
    [SerializeField] private GeminiClient gc;

    private bool _isRecording = false;

    private void OnEnable()
    {
        // Subscribe to the component's stop event
        if (microphoneRecord != null)
        {
            microphoneRecord.OnRecordStop += OnMicrophoneRecordStop;
        }
    }

    private void OnDisable()
    {
        // Unsubscribe to avoid memory leaks
        if (microphoneRecord != null)
        {
            microphoneRecord.OnRecordStop -= OnMicrophoneRecordStop;
        }
    }

    private async void Start()
    {
        if (whisperManager != null)
        {
            Debug.Log("Whisper 모델 로딩 시작...");

            // 게임 시작 시 모델이 로드될 때까지 기다립니다.
            await whisperManager.InitModel();

            Debug.Log("Whisper 모델 로딩 완료! 이제 녹음이 가능합니다.");
        }
        else
        {
            Debug.LogError("WhisperManager가 스크립트에 할당되지 않았습니다!");
        }
    }

    async void Update()
    {
        // Press Spacebar to toggle recording
        if (Input.GetKeyDown(KeyCode.Space))
        {
            if (!_isRecording)
            {
                gc.StartListening();
                StartListening();
            }
            else
            {
                gc.StopListening();
                StopListening();
            }
        }
    }

    private void StartListening()
    {
        _isRecording = true;
        Debug.Log("Listening started... Speak in Korean.");

        if (microphoneRecord != null)
        {
            microphoneRecord.StartRecord();
        }
    }

    private void StopListening()
    {
        _isRecording = false;
        Debug.Log("Stopping microphone and preparing transcription...");

        if (microphoneRecord != null)
        {
            // This triggers the OnRecordStop event below
            microphoneRecord.StopRecord();
        }
    }

    // This callback receives the recorded data wrapper safely from the framework
    private async void OnMicrophoneRecordStop(AudioChunk recordedChunk)
    {
        Debug.Log("Processing Korean audio via Whisper...");

        // 기본 생성자 대신 static 메서드로 파라미터를 생성합니다.
        var paramsObj = WhisperParams.GetDefaultParams();
        paramsObj.Language = "ko"; // 한국어 강제 지정

        // Whisper 인퍼런스 시작
        var result = await whisperManager.GetTextAsync(recordedChunk.Data, recordedChunk.Frequency, recordedChunk.Channels);

        if (result != null)
        {
            Debug.Log($"Transcribed Text (KO): {result.Result}");
            gc.SendUserSpeech(result.Result);
        }
        else
        {
            Debug.LogError("Transcription failed.");
        }
    }
}