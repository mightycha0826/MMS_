# MMS_ — 미소년 면접 시뮬레이터

대학 입시 면접을 AI 면접관과 함께 실전처럼 연습할 수 있는 Unity WebGL 기반 시뮬레이터입니다.

---

## 프로젝트 구조

```
MMS_/
├── Scripts/                       # Unity C# 스크립트
│   ├── GeminiClient.cs                # relay WebSocket 통신 클라이언트
│   ├── PacketDefinition.cs            # 패킷 구조 정의 (ClientMsg / ServerMsg)
│   ├── InterviewerController.cs       # AI 면접관 캐릭터 (감정, 애니메이션)
│   ├── SubtitleManager.cs             # 자막 출력 (음절 단위 타이핑 효과)
│   ├── MicDisplayController.cs        # 마이크 입력 시각화 (웨이브 바)
│   ├── StartscreenController.cs       # 시작화면 관리
│   ├── IntroAnimator.cs               # 인트로 애니메이션
│   └── TestTrigger.cs                 # 개발용 테스트 스크립트
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
[Unity — 정해운찬/정형태 담당]              [gemini-relay — 차유근 담당]
        │                                           │
  GeminiClient.cs                             index.ts
  (WebSocket + JSON)   ──wss://──────▶   (Cloudflare Worker)
                                                    │
                                          ┌─────────┴──────────┐
                                          ▼                     ▼
                                     Gemini API           HuggingFace
                                    (답변 분석)          (LoRA 모델 추론)
```

- **Unity** : 면접 UI, Web Speech API로 STT/TTS 처리
- **gemini-relay** : Cloudflare Worker 중계 서버, AI 판단 로직 담당
- **Gemini 2.5 Flash** : 지원자 답변 품질 분석
- **HuggingFace LoRA** : 다음 질문 결정 (`ai-mms/ai-gemma-lora-model`)

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
1. [Unity]   Web Speech API로 지원자 음성 → 텍스트 변환 (STT)
2. [Unity]   GeminiClient.cs → relay로 전송 (wss://)
3. [relay]   Gemini API에 답변 분석 요청
             → "'키워드' 언급했으나 메커니즘 설명 없음. 꼬리질문으로 검증 필요."
4. [relay]   HuggingFace LoRA 모델에 다음 행동 결정 요청
             → { text, decision, emotionLabel }
5. [Unity]   결과 수신 → 자막 출력 + 면접관 감정 변경 + TTS 재생
```

---

## WebSocket 프로토콜

### Unity → relay

```json
// 1. 면접 세션 시작
{ "type": "session_start", "last_question": "자기소개 해주세요" }

// 2. 지원자 답변 전송 (Web Speech API 결과)
{ "type": "user_speech", "text": "안녕하세요, 저는..." }
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
  "content": {
    "text": "딥러닝에서 역전파 알고리즘을 직접 구현해본 경험이 있나요?",
    "decision": "follow_up",
    "emotionLabel": "호기심/탐색"
  },
  "stt_result": "안녕하세요, 저는...",
  "usage": { "timestamp": "2026-..." }
}

// 에러
{ "type": "error", "message": "에러 내용" }
```

| `decision` | 의미 |
|-----------|------|
| `follow_up` | 꼬리질문 — 같은 주제에서 더 깊이 파고들기 |
| `next_topic` | 주제 전환 — 다음 평가 항목으로 이동 |

---

## 면접관 감정 레이블

| 감정 | 레이블 예시 | 아바타 액션 |
|------|-----------|------------|
| 압박 | `날카로움/압박`, `압박/재질문` | avatar_stern |
| 호기심 | `호기심/탐색`, `호기심/기대` | avatar_curious |
| 기쁨 | `기쁨/격려`, `기쁨/지지` | avatar_smile |
| 당혹 | `당혹/재질문`, `당혹/확인` | avatar_tilt |
| 중립 | `중립/전환`, `중립/마무리` | avatar_neutral |
| 정중 | `정중함/마무리`, `정중/전환` | avatar_nod |

---

## Unity 스크립트 역할

| 스크립트 | 역할 |
|---------|------|
| `GeminiClient` | relay WebSocket 연결, 패킷 송수신, 세션 관리 |
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
| 배포 | Cloudflare Workers (`wss://gemini-relay.mightycha0826.workers.dev`) |

---

## 관련 레포

- **gemini-relay** (중계 서버) : https://github.com/mightycha0826/gemini-relay
