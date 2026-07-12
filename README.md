# MMS_ — 미소년 면접 시뮬레이터

대학 입시 면접을 AI 면접관과 함께 실전처럼 연습할 수 있는 Unity WebGL 기반 시뮬레이터입니다.

---

## 프로젝트 구조

```
MMS_/
├── Scripts/                       # Unity C# 스크립트
│   ├── GeminiClient.cs                # relay WebSocket 통신 클라이언트
│   ├── SupertoneTtsClient.cs          # Supertone TTS 직접 호출 (Unity ↔ Supertone)
│   ├── PacketDefinition.cs            # 패킷 구조 정의 (ClientMsg / ServerMsg)
│   ├── InterviewerController.cs       # AI 면접관 캐릭터 (감정, 애니메이션)
│   ├── SubtitleManager.cs             # 자막 출력 (음절 단위 타이핑 효과)
│   ├── MicDisplayController.cs        # 마이크 입력 시각화 (웨이브 바)
│   ├── StartscreenController.cs       # 시작화면 관리
│   ├── IntroAnimator.cs               # 인트로 애니메이션
│   └── TestTrigger.cs                 # 개발용 테스트 스크립트
│
├── StreamingAssets/
│   └── supertone_config.sample.json   # Supertone apiKey/voiceId 설정 템플릿 (실제 파일은 git-ignored)
│
└── ai/                            # AI 파이프라인 (면접 시작 전 1회 실행)
    ├── interview_question_generator.py    # PDF 분석 → 첫 질문 생성 파이프라인
    ├── adapter_config.json                # LoRA 구조 설정
    ├── adapter_model.safetensors          # 학습된 가중치
    └── tokenizer_config.json              # 토크나이저 설정
```

중계 서버는 별도 레포로 관리합니다 → [gemini-relay](https://github.com/mightycha0826/gemini-relay)

---

## 전체 아키텍처

```
[Unity — 정해운찬/정형태 담당]              [gemini-relay — 차유근 담당]              [interview_question_generator.py]
        │                                           │                                          │
  GeminiClient.cs                             index.ts                                   Gemini + Gemma LoRA
  (WebSocket + JSON)   ──wss://──────▶   (Cloudflare Worker, 중계 전용)   ──────▶   (질문/감정 분석 전담)
        │
        └── SupertoneTtsClient.cs  ──https://──▶  Supertone TTS API (Unity가 직접 호출)
```

- **Unity** : 면접 UI, Web Speech API로 STT 처리, Supertone TTS를 직접 호출해 오디오 재생
- **gemini-relay** : Cloudflare Worker, Unity ↔ Python 간 메시지 중계만 담당 (AI 호출·TTS 없음)
- **interview_question_generator.py** : Gemini 답변 분석 + Gemma LoRA 질문 생성, 감정/학과 판단을 전담
- **Supertone TTS** : `content.text`를 받아 Unity가 직접 호출, relay는 관여하지 않음

> ⚠️ Supertone API 키는 WebGL 빌드에 그대로 노출됩니다 (브라우저 개발자 도구로 확인 가능). relay를 거치지 않기로 한 트레이드오프이며, Supertone 콘솔에서 도메인 제한/사용량 제한을 걸어두는 걸 권장합니다.

---

## AI 파이프라인 (면접 시작 전)

`ai/interview_question_generator.py`를 면접 시작 전 1회 실행해 첫 질문을 준비합니다.

```
PDF 자료
  │
  ▼
Gemini 2.5 Flash — 문서 분석
  → keywords, topics, gemini_summary, gemma_hint, opening_remark
  │
  ▼
Gemma-2b-it + LoRA — 첫 질문 초안 생성
  │
  ▼
Gemini 2.5 Flash — 질문 검수 + opening_remark 인라인 결합
  → 최종 첫 질문 (면접관 첫 마디 포함)
```

```bash
python ai/interview_question_generator.py --file 자료.pdf --adapter . --GEMINI_API_KEY "MY_KEY"
```

---

## 면접 1턴 흐름 (relay 연동)

```
1. [Unity]     Web Speech API로 지원자 음성 → 텍스트 변환 (STT)
2. [Unity]     GeminiClient.cs → relay로 client_msg 전송 (department + last_question + text, 매 턴 재전송)
3. [relay]     Python(interview_question_generator.py)으로 메시지 중계
4. [Python]    Gemini로 답변 분석 → Gemma LoRA로 다음 질문/감정 결정
               → { text, emotion: { label, score, intensity, action } }
5. [Unity]     결과 수신 → 자막 출력 + 면접관 감정 변경
               → content.text를 다음 턴 last_question으로 저장해뒀다가 다시 전송
6. [Unity]     SupertoneTtsClient.cs가 content.text로 Supertone TTS를 직접 호출 → 오디오 재생
               (실패 시 자막만 표시된 채로 계속 진행, 면접 흐름을 막지 않음)
```

---

## WebSocket 프로토콜

### Unity → relay

매 턴마다 `client_msg` 하나로 통합해서 보냄. 서버가 세션 상태를 기억하지 않으므로 `last_question`을 매번 다시 실어야 함.

```json
{
  "type": "client_msg",
  "department": "인공지능학과",
  "last_question": "자기소개 해주세요",
  "text": "안녕하세요, 저는..."
}
```

### relay → Unity

```json
// 연결 준비 완료
{ "type": "ready" }

// 처리 중
{ "type": "processing" }

// 최종 결과
{
  "type": "server_content",
  "message_id": "uuid",
  "client_session_id": "라우팅용 ID",
  "content": {
    "text": "딥러닝에서 역전파 알고리즘을 직접 구현해본 경험이 있나요?",
    "emotion": {
      "label": "serious",
      "score": 0.8,
      "intensity": "medium",
      "action": "avatar_stern"
    }
  },
  "gemini_analysis": {
    "dept": "인공지능학과",
    "dept_reasoning": "...",
    "keywords": ["역전파", "경사하강법"],
    "summary": "..."
  },
  "usage": { "timestamp": "2026-..." }
}

// 에러 (Python AI 프로세스 미연결 등)
{ "type": "error", "message": "AI worker not connected" }
```

> `decision`, `emotionLabel`(한글), `stt_result`, `content.audio` 필드는 이 프로토콜에서 삭제되었습니다.
> TTS 오디오는 더 이상 relay가 만들어주지 않으며, Unity가 `content.text`로 Supertone을 직접 호출합니다.

---

## Supertone TTS (Unity가 직접 호출)

relay를 거치지 않고 `SupertoneTtsClient.cs`가 `content.text`를 받은 직후 Supertone API를 직접 호출합니다.

**요청**

```
POST https://supertoneapi.com/v1/text-to-speech/{voiceId}/stream
x-sup-api-key: <API 키>
Content-Type: application/json
Accept: audio/wav

{ "text": "...", "language": "ko", "model": "sona_speech_1" }
```

`style` 파라미터는 voice마다 지원 목록이 달라 400 에러를 유발할 수 있어 기본적으로 넣지 않습니다. 필요하면 `GET /v1/voices/{voiceId}`로 지원 style을 먼저 확인하세요.

**설정 (apiKey / voiceId)**

`StreamingAssets/supertone_config.sample.json`을 같은 폴더에 `supertone_config.json`으로 복사하고 값을 채워 넣으세요 (`.gitignore` 처리되어 있어 git에는 올라가지 않습니다).

```json
{
  "apiKey": "YOUR_SUPERTONE_API_KEY",
  "voiceId": "YOUR_VALID_VOICE_ID"
}
```

- `voiceId`는 반드시 이 API 키로 접근 가능한 voice여야 합니다. `GET /v1/voices/search?language=ko`로 접근 가능한 voice 목록을 먼저 확인하세요.
- 오디오 생성 실패(네트워크 오류, voice 접근 불가, WAV 디코딩 실패 등) 시 예외 없이 건너뛰고 자막만 표시됩니다 — 면접 진행이 막히지 않습니다.

**⚠️ 보안 트레이드오프**: WebGL 빌드는 클라이언트 사이드라 API 키가 브라우저 개발자 도구로 노출될 수 있습니다. relay를 거치지 않기로 한 의도적인 결정이며, Supertone 콘솔에서 도메인 제한/사용량 제한(rate limit)을 걸어 리스크를 줄이는 걸 권장합니다.

**⚠️ CORS 주의**: 브라우저(WebGL 빌드)에서 `supertoneapi.com`으로 직접 요청을 보내므로, Supertone API가 브라우저 Origin에 대해 `Access-Control-Allow-Origin` / `Access-Control-Allow-Headers: x-sup-api-key`를 허용하지 않으면 요청이 CORS로 차단될 수 있습니다. 실제 WebGL 빌드로 반드시 검증하세요 (Unity 에디터에서는 CORS가 적용되지 않아 통과해도 브라우저에서는 막힐 수 있습니다).

---

## 면접관 감정 레이블

`emotion.label`은 `InterviewerController.InterviewerMood` enum과 동일한 이름(대소문자 무관)으로 옵니다.

| `emotion.label` | 아바타 액션 |
|-----------------|------------|
| `pressuring` | avatar_stern |
| `neutral` | avatar_neutral |
| `smile` | avatar_smile |
| `shy` | avatar_shy |
| `serious` | avatar_serious |
| `confused` | avatar_tilt |
| `satisfied` | avatar_nod |

---

## Unity 스크립트 역할

| 스크립트 | 역할 |
|---------|------|
| `GeminiClient` | relay WebSocket 연결, 패킷 송수신, 세션 관리 |
| `SupertoneTtsClient` | Supertone TTS 직접 호출 및 오디오 재생 (설정 로드, WAV 디코딩, 실패 시 폴백) |
| `PacketDefinition` | Unity ↔ relay 패킷 구조 정의 |
| `InterviewerController` | 면접관 캐릭터 감정 표현 (7종), 말하는 입 애니메이션, 호흡 이펙트 |
| `SubtitleManager` | 한글 음절 단위 타이핑 효과, 텍스트 오버플로우 시 패널 자동 확장 |
| `MicDisplayController` | 13개 바 웨이브 애니메이션 (Idle / Listening / Done) |
| `StartscreenController` | 랜덤 배경, blur 패널 애니메이션, 메인화면 전환 시퀀스 |
| `IntroAnimator` | 시작화면 → 메인화면 페이드인 + 슬라이드 애니메이션 |

---

## 기술 스택

| 분류 | 기술 |
|------|------|
| 클라이언트 | Unity WebGL, C#, Web Speech API |
| 중계 서버 | Cloudflare Workers, TypeScript |
| 답변 분석 | Google Gemini 2.5 Flash |
| AI 추론 | HuggingFace Inference API, PEFT LoRA (Gemma-2b-it) |
| TTS | Supertone TTS API (Unity에서 직접 호출) |
| 배포 | Cloudflare Workers (`wss://gemini-relay.mightycha0826.workers.dev`) |

---

## 관련 레포

- **gemini-relay** (중계 서버) : https://github.com/mightycha0826/gemini-relay
