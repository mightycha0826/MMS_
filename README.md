# MMS_ — 미소년 면접 시뮬레이터

대학 입시 면접을 AI 면접관과 함께 실전처럼 연습할 수 있는 Unity WebGL 기반 시뮬레이터입니다.

---

## 프로젝트 구조

```
MMS_/
├── Scripts/                  # Unity C# 스크립트
│   ├── InterviewerController.cs   # AI 면접관 캐릭터 (감정, 애니메이션)
│   ├── SubtitleManager.cs         # 자막 출력 (음절 단위 타이핑 효과)
│   ├── MicDisplayController.cs    # 마이크 입력 시각화 (웨이브 바)
│   ├── StartscreenController.cs   # 시작화면 관리
│   ├── IntroAnimator.cs           # 인트로 애니메이션
│   └── TestTrigger.cs             # 개발용 테스트 스크립트
│
└── ai/                       # LoRA 파인튜닝 모델
    ├── adapter_config.json        # LoRA 구조 설정
    ├── adapter_model.safetensors  # 학습된 가중치 (38MB)
    ├── lora_train_data.jsonl      # 학습 데이터
    ├── lora_train_data_val.jsonl  # 검증 데이터
    └── tokenizer_config.json      # 토크나이저 설정
```

중계 서버는 별도 레포로 관리합니다 → [gemini-relay](https://github.com/mightycha0826/gemini-relay)

---

## 전체 아키텍처

```
[Unity WebGL — 정해운찬/정형태 담당]          [gemini-relay — 차유근 담당]
        │                                    │
  GeminiClient.cs                       index.ts
  (WebSocket + JSON)   ──wss://──▶   (Cloudflare Worker)
                                            │
                                    ┌───────┴────────┐
                                    ▼                ▼
                               Gemini API      HuggingFace
                             (텍스트 분석)    (LoRA 모델 추론)
```

- **Unity WebGL** : 면접 UI, Web Speech API로 STT/TTS 처리
- **gemini-relay** : Cloudflare Worker 중계 서버, AI 판단 로직 담당
- **Gemini API** : 지원자 답변 품질 분석
- **HuggingFace LoRA** : 다음 질문 결정 (`ai-mms/ai-gemma-lora-model`)

---

## 면접 1턴 흐름

```
1. [Unity]   Web Speech API로 지원자 음성 → 텍스트 변환 (STT)
2. [Unity]   텍스트를 relay로 전송 (wss://)
3. [relay]   Gemini API에 답변 분석 요청
             → "'{키워드}' 언급했으나 메커니즘 설명 없음. 꼬리질문으로 검증 필요."
4. [relay]   HuggingFace LoRA 모델에 다음 행동 결정 요청
             → { text: "다음 질문", decision: "follow_up", emotion: {...} }
5. [Unity]   결과 수신 → 자막 출력 + 면접관 감정 변경 + TTS 재생
```

---

## WebSocket 프로토콜

### Unity → relay

```json
// 1. 면접 턴 시작
{ "type": "session_start", "department": "컴퓨터공학부", "last_question": "자기소개 해주세요" }

// 2. 지원자 답변 전송 (Web Speech API 결과)
{ "type": "user_speech", "text": "안녕하세요, 저는..." }
```

### relay → Unity

```json
// 처리 중
{ "type": "processing" }

// 최종 결과
{
  "type": "server_content",
  "message_id": "uuid",
  "content": {
    "text": "딥러닝에서 역전파 알고리즘을 설명해주세요",
    "decision": "follow_up",
    "emotion": { "label": "날카로움/압박", "score": 0.83, "intensity": "medium" },
    "is_final": false
  },
  "stt_result": "안녕하세요, 저는...",
  "usage": { "timestamp": "2026-..." }
}
```

| `decision` | 의미 |
|-----------|------|
| `follow_up` | 꼬리질문 — 같은 주제에서 더 깊이 파고들기 |
| `next_topic` | 주제 전환 — 다음 평가 항목으로 이동 |

---

## AI 모델

### LoRA 파인튜닝 모델
- **베이스 모델** : `google/gemma-2b-it`
- **HuggingFace** : `ai-mms/ai-gemma-lora-model`
- **학습 목적** : 실제 대학 입시 면접 데이터 기반으로 면접관 행동 결정
- **LoRA 설정** : rank 16, alpha 32, dropout 0.05

### 면접관 감정 종류

| 레이블 | 상황 |
|--------|------|
| 날카로움/압박 | 핵심 키워드 검증 필요 |
| 분석적_의심 | 답변 완성도 낮음 |
| 탐색/검증 | 추가 설명 요구 |
| 중립/전환 | 주제 전환 시 |
| 정중함/마무리 | 면접 마무리 |

---

## Unity 스크립트 역할

| 스크립트 | 역할 |
|---------|------|
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
| 음성 분석 | Google Gemini 2.0 Flash |
| AI 추론 | HuggingFace Inference API, PEFT LoRA |
| 배포 | Cloudflare Workers (`wss://gemini-relay.mightycha0826.workers.dev`) |

---

## 관련 레포

- **gemini-relay** (중계 서버) : https://github.com/mightycha0826/gemini-relay
