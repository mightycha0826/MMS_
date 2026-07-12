using System;
using System.Collections;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

public class VoiceGenerator : MonoBehaviour
{
    [Header("Supertone API Settings")]
    [SerializeField] private string apiKey = "APIKEY";
    [SerializeField] private string voiceId = "VOICEID";

    [Header("Audio Component")]
    [SerializeField] private AudioSource audioSource;

    // API 요청에 보낼 데이터 구조체
    [Serializable]
    public class TTSRequestData
    {
        public string text;
        public string language; // ex) "ko", "en", "ja"
    }

    private void Start()
    {
        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();
    }

    /// <summary>
    /// 버튼 이벤트나 다른 스크립트에서 호출할 공개 메서드
    /// </summary>
    public async void GenerateAndPlaySpeech(string textToSpeak, Action startSpeakHandler=null, Action endSpeakHandler=null)
    {
        // Debug.Log("수퍼톤 TTS 요청 시작...");
        AudioClip clip = await RequestTTSAsync(textToSpeak);

        if (clip != null && audioSource != null)
        {
            startSpeakHandler?.Invoke();
            StartCoroutine(DelayAction(clip.length, endSpeakHandler));
            audioSource.clip = clip;
            audioSource.pitch = 1.06f;
            audioSource.Play();
            // Debug.Log("음성 재생 시작!");
        }
    }

    IEnumerator DelayAction(float delay, Action action)
    {
        yield return new WaitForSeconds(delay);
        action?.Invoke();
    }

    /// <summary>
    /// 수퍼톤 API 서버에 텍스트 전송 후 AudioClip을 받아오는 비동기 메서드
    /// </summary>
    private async Task<AudioClip> RequestTTSAsync(string targetText)
    {
        string url = $"https://supertoneapi.com/v1/text-to-speech/{voiceId}";

        TTSRequestData requestBody = new TTSRequestData
        {
            text = targetText,
            language = "ko"
        };

        string jsonPayload = JsonUtility.ToJson(requestBody);
        byte[] jsonBytes = Encoding.UTF8.GetBytes(jsonPayload);

        using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(jsonBytes);
            request.downloadHandler = new DownloadHandlerBuffer();

            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("x-sup-api-key", apiKey);

            var operation = request.SendWebRequest();
            while (!operation.isDone)
            {
                await Task.Yield();
            }

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"Supertone API 오류 발생: {request.error}\n응답 내용: {request.downloadHandler.text}");
                return null;
            }

            byte[] audioData = request.downloadHandler.data;
            return WebRequestUtils.WavToAudioClip(audioData);
        }
    }
}

/// <summary>
/// 바이트 배열(WAV) 데이터를 유니티 AudioClip으로 파싱해주는 유틸리티 클래스
/// </summary>
public static class WebRequestUtils
{
    public static AudioClip WavToAudioClip(byte[] wavBytes)
    {
        // WAV 파일 헤더 규격에서 샘플 수, 채널 수, 샘플 레이트 위치 추출
        int channels = BitConverter.ToInt16(wavBytes, 22);
        int sampleRate = BitConverter.ToInt32(wavBytes, 24);

        // 44바이트 헤더 이후부터 실제 오디오 데이터(PCM)가 시작
        int pos = 12;
        while (pos < wavBytes.Length - 8)
        {
            if (wavBytes[pos] == 'd' && wavBytes[pos + 1] == 'a' && wavBytes[pos + 2] == 't' && wavBytes[pos + 3] == 'a')
            {
                pos += 4;
                break;
            }
            pos++;
        }

        int subChunk2Size = BitConverter.ToInt32(wavBytes, pos);
        pos += 4;

        int sampleCount = subChunk2Size / 2; // 16비트(2바이트) 기준
        float[] audioFloats = new float[sampleCount];

        for (int i = 0; i < sampleCount; i++)
        {
            short bit16Sample = BitConverter.ToInt16(wavBytes, pos + (i * 2));
            audioFloats[i] = bit16Sample / 32768.0f; // float 범위(-1.0 ~ 1.0)로 정규화
        }

        AudioClip audioClip = AudioClip.Create("SupertoneTTS_Audio", sampleCount / channels, channels, sampleRate, false);
        audioClip.SetData(audioFloats, 0);
        return audioClip;
    }
}
