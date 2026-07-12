using System;
using System.Collections;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Supertone TTS를 Unity에서 직접 호출해 재생하는 컴포넌트.
/// relay는 TTS를 처리하지 않으므로, content.text를 받은 직후 여기서 오디오를 생성/재생한다.
///
/// API 키는 StreamingAssets/{configFileName} (기본값: supertone_config.json)에서 런타임에 읽어온다.
/// 이 파일은 git에 커밋하지 않는다 (.gitignore 처리됨) — supertone_config.sample.json을 복사해서 채워 넣을 것.
/// WebGL 빌드 특성상 클라이언트에 노출되는 API 키 자체를 막을 수는 없으므로,
/// Supertone 콘솔에서 도메인 제한/사용량 제한을 걸어두는 걸 권장한다.
/// </summary>
public class VoiceGenerator : MonoBehaviour
{
    [Header("Supertone 설정 파일 (StreamingAssets, git-ignored)")]
    [SerializeField] private string configFileName = "supertone_config.json";

    [Header("Audio Component")]
    [SerializeField] private AudioSource audioSource;

    private const string ApiBaseUrl = "https://supertoneapi.com/v1/text-to-speech";

    private string _apiKey;
    private string _voiceId;
    private bool _configLoaded;
    private bool _configLoadAttempted;

    private void Start()
    {
        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();
    }

    /// <summary>
    /// 주어진 텍스트로 TTS 오디오를 생성해 재생한다.
    /// TTS 생성에 실패해도(설정 없음, 네트워크 오류 등) startSpeakHandler는 항상 호출된다 —
    /// 자막/감정 표시/대화 기록이 오디오 유무와 무관하게 항상 진행되어야 하기 때문이다.
    /// </summary>
    public async void GenerateAndPlaySpeech(string textToSpeak, Action startSpeakHandler = null, Action endSpeakHandler = null)
    {
        AudioClip clip = await RequestTTSAsync(textToSpeak);

        startSpeakHandler?.Invoke();

        if (clip != null && audioSource != null)
        {
            audioSource.clip = clip;
            audioSource.pitch = 1.06f;
            audioSource.Play();
            StartCoroutine(DelayAction(clip.length, endSpeakHandler));
        }
        else
        {
            // 오디오가 없으면 기다릴 재생 시간이 없으므로 즉시 종료 콜백 실행
            endSpeakHandler?.Invoke();
        }
    }

    private IEnumerator DelayAction(float delay, Action action)
    {
        yield return new WaitForSeconds(delay);
        action?.Invoke();
    }

    /// <summary>Supertone API에 텍스트를 전송해 AudioClip을 받아오는 비동기 메서드. 실패 시 null 반환.</summary>
    private async Task<AudioClip> RequestTTSAsync(string targetText)
    {
        if (string.IsNullOrEmpty(targetText)) return null;

        if (!_configLoaded && !_configLoadAttempted)
            await LoadConfigAsync();

        if (!_configLoaded)
        {
            Debug.LogWarning("[VoiceGenerator] Supertone 설정이 없어 TTS를 건너뜁니다 (자막만 표시됩니다).");
            return null;
        }

        string url = $"{ApiBaseUrl}/{_voiceId}/stream";
        string jsonPayload = JsonUtility.ToJson(new TTSRequestData
        {
            text = targetText,
            language = "ko",
            model = "sona_speech_1",
        });
        byte[] jsonBytes = Encoding.UTF8.GetBytes(jsonPayload);

        using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(jsonBytes);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Accept", "audio/wav");
            request.SetRequestHeader("x-sup-api-key", _apiKey);

            var operation = request.SendWebRequest();
            while (!operation.isDone)
                await Task.Yield();

            if (request.result != UnityWebRequest.Result.Success)
            {
                // 네트워크 오류, voice 접근 불가 등 - 면접 진행을 막지 않도록 자막만 표시된 상태로 계속 진행
                Debug.LogWarning($"[VoiceGenerator] Supertone TTS 요청 실패 ({request.responseCode}): {request.error} — 자막만 표시됩니다.");
                return null;
            }

            return WebRequestUtils.WavToAudioClip(request.downloadHandler.data);
        }
    }

    // ─── 설정 로드 (StreamingAssets) ───────────────────────────────────────

    private async Task LoadConfigAsync()
    {
        _configLoadAttempted = true;

        string path = Path.Combine(Application.streamingAssetsPath, configFileName);
        if (!path.Contains("://"))
            path = "file://" + path;

        using (var req = UnityWebRequest.Get(path))
        {
            var operation = req.SendWebRequest();
            while (!operation.isDone)
                await Task.Yield();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[VoiceGenerator] 설정 파일을 불러오지 못했습니다 ({path}): {req.error}");
                return;
            }

            try
            {
                var config = JsonUtility.FromJson<SupertoneConfig>(req.downloadHandler.text);
                if (config == null || string.IsNullOrEmpty(config.apiKey) || string.IsNullOrEmpty(config.voiceId))
                {
                    Debug.LogWarning("[VoiceGenerator] 설정 파일에 apiKey/voiceId가 비어 있습니다.");
                    return;
                }

                _apiKey = config.apiKey;
                _voiceId = config.voiceId;
                _configLoaded = true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[VoiceGenerator] 설정 파싱 실패: {e.Message}");
            }
        }
    }

    [Serializable] private class SupertoneConfig { public string apiKey; public string voiceId; }
    [Serializable] private class TTSRequestData { public string text; public string language; public string model; }
}

/// <summary>바이트 배열(WAV) 데이터를 Unity AudioClip으로 파싱하는 유틸리티 클래스 (PCM16 전용).</summary>
public static class WebRequestUtils
{
    public static AudioClip WavToAudioClip(byte[] wavBytes)
    {
        if (wavBytes == null || wavBytes.Length < 44) return null;

        int channels = BitConverter.ToInt16(wavBytes, 22);
        int sampleRate = BitConverter.ToInt32(wavBytes, 24);
        int bitsPerSample = BitConverter.ToInt16(wavBytes, 34);

        int dataOffset = FindDataChunkOffset(wavBytes);
        if (dataOffset < 0 || bitsPerSample != 16 || channels <= 0)
        {
            Debug.LogWarning($"[WebRequestUtils] 지원하지 않는 WAV 형식 (bits={bitsPerSample}, channels={channels})");
            return null;
        }

        int subChunk2Size = BitConverter.ToInt32(wavBytes, dataOffset - 4);
        int sampleCount = subChunk2Size / 2; // 16비트(2바이트) 기준
        float[] audioFloats = new float[sampleCount];

        for (int i = 0; i < sampleCount; i++)
        {
            short bit16Sample = BitConverter.ToInt16(wavBytes, dataOffset + i * 2);
            audioFloats[i] = bit16Sample / 32768.0f; // float 범위(-1.0 ~ 1.0)로 정규화
        }

        AudioClip audioClip = AudioClip.Create("SupertoneTTS_Audio", sampleCount / channels, channels, sampleRate, false);
        audioClip.SetData(audioFloats, 0);
        return audioClip;
    }

    private static int FindDataChunkOffset(byte[] data)
    {
        for (int i = 12; i <= data.Length - 8; i++)
        {
            if (data[i] == 'd' && data[i + 1] == 'a' && data[i + 2] == 't' && data[i + 3] == 'a')
                return i + 8;
        }
        return -1;
    }
}
