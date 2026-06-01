"""
설치:
  pip install google-genai transformers peft torch accelerate pymupdf sentencepiece websocket-client

사용법:
  python ws_sender.py --adapter . --gemini-key "YOUR_KEY"          # 서버 모드 (상시 대기)
  python ws_sender.py --demo                                        # 데모 1회 실행
  python ws_sender.py --demo --session-id "beatus-test-001"
"""

import argparse
import copy
import json
import os
import re
import threading
import time
import uuid
from datetime import datetime, timezone
from pathlib import Path

import websocket  # pip install websocket-client
from google import genai
from google.genai import types
from peft import PeftModel
from transformers import AutoTokenizer, AutoModelForCausalLM
import torch


# ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
# 설정
# ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

GEMINI_API_KEY    = os.environ.get("GEMINI_API_KEY", "YOUR_GEMINI_API_KEY_HERE")
LORA_ADAPTER_PATH = os.environ.get("LORA_ADAPTER_PATH", "./lora_adapter")
BASE_MODEL_ID     = "google/gemma-2b-it"

WS_BASE_URL     = "wss://gemini-relay.mightycha0826.workers.dev"
WS_ENDPOINT     = "/ws"
RECONNECT_DELAY = 3
MAX_RECONNECT   = 5
SYSTEM_PROMPT = (
    "당신은 대학 입시 면접관 AI입니다.\n"
    "Gemini 음성 분석 결과와 직전 면접 맥락을 입력받아,\n"
    "다음 행동을 결정하고 JSON 패킷 하나만 출력하십시오.\n\n"
    "판단 기준:\n"
    "  follow_up  : 답변이 모호하거나 핵심 키워드 검증이 필요한 경우 → 날카로운 꼬리질문\n"
    "  next_topic : 답변이 충분히 구체적이거나 새 섹션으로 이동할 경우 → 자연스러운 전환\n\n"
    "출력은 반드시 유효한 JSON 하나만 생성하십시오. 설명/마크다운 절대 금지."
)

ALLOWED_EMOTIONS = {"neutral", "smile", "shy", "serious", "confused", "pressuring", "satisfied"}

_EMOTION_MAP: dict[str, str] = {
    "중립":          "neutral",   "neutral":       "neutral",
    "전환":          "neutral",   "중립/전환":     "neutral",
    "smile":         "smile",     "격려":          "smile",
    "지지":          "smile",     "정중함":        "smile",
    "shy":           "shy",       "부끄러움":      "shy",
    "serious":       "serious",   "진지":          "serious",
    "마무리":        "serious",
    "confused":      "confused",  "혼란":          "confused",
    "당혹":          "confused",  "재질문":        "confused",
    "pressuring":    "pressuring","압박":          "pressuring",
    "날카로움":      "pressuring","날카로움/압박": "pressuring",
    "날카룸/압박":   "pressuring",
    "satisfied":     "satisfied", "만족":          "satisfied",
}

_ACTION_MAP: dict[str, str] = {
    "neutral":    "avatar_neutral",
    "smile":      "avatar_smile",
    "shy":        "avatar_shy",
    "serious":    "avatar_serious",
    "confused":   "avatar_tilt",
    "pressuring": "avatar_stern",
    "satisfied":  "avatar_nod",
}


def normalize_emotion(raw_label: str) -> str:
    if raw_label in _EMOTION_MAP:
        return _EMOTION_MAP[raw_label]
    for key, emotion in _EMOTION_MAP.items():
        if key in raw_label:
            return emotion
    if raw_label.lower() in ALLOWED_EMOTIONS:
        return raw_label.lower()
    return "neutral"


def normalize_packet(packet: dict) -> dict:
    p = copy.deepcopy(packet)
    emotion    = p.get("content", {}).get("emotion", {})
    normalized = normalize_emotion(emotion.get("label", "neutral"))
    emotion["label"]  = normalized
    emotion["action"] = _ACTION_MAP.get(normalized, "avatar_neutral")
    p["content"]["emotion"] = emotion
    if not p.get("message_id"):
        p["message_id"] = str(uuid.uuid4())
    p["sent_at"] = datetime.now(timezone.utc).isoformat()
    return

def extract_pdf_text(file_path: str) -> str:
    import fitz
    doc = fitz.open(file_path)
    return "\n".join(page.get_text() for page in doc)

GEMINI_RESPONSE_SCHEMA = {
    "type": "object",
    "properties": {
        "dept":           {"type": "string"},
        "dept_reasoning": {"type": "string"},
        "keywords":       {"type": "array", "items": {"type": "string"}},
        "gemini_summary": {"type": "string"},
    },
    "required": ["dept", "dept_reasoning", "keywords", "gemini_summary"],
}

GEMINI_PROMPT_TMPL = """당신은 대학 입시 면접 보조 AI입니다.
아래 문서를 분석하여 JSON으로 응답하십시오.

- dept: 문서에서 명시된 지원 학과. 없으면 내용으로 가장 적합한 학과 추론.
- dept_reasoning: 학과 추론 근거 한 문장.
- keywords: 핵심 키워드 3개.
- gemini_summary: 면접관 AI에게 전달할 분석 요약. 핵심 개념과 심층 검증이 필요한 부분 포함. 200자 이내 한국어.

문서 내용:
{doc_text}
"""

# ClientMsg 기반 실시간 답변 분석용 Gemini 프롬프트
GEMINI_ANSWER_SCHEMA = {
    "type": "object",
    "properties": {
        "keywords":       {"type": "array", "items": {"type": "string"}},
        "gemini_summary": {"type": "string"},
        "answer_quality": {"type": "string", "enum": ["충분", "모호", "불충분"]},
    },
    "required": ["keywords", "gemini_summary", "answer_quality"],
}

GEMINI_ANSWER_TMPL = """당신은 대학 입시 면접 보조 AI입니다.
지원자의 답변을 분석하여 JSON으로 응답하십시오.

- keywords: 답변에서 추출한 핵심 키워드 최대 3개.
- gemini_summary: 면접관 AI에게 전달할 답변 분석 요약. 심층 검증이 필요한 부분 명시. 200자 이내 한국어.
- answer_quality: 답변 완성도 평가. "충분"(next_topic 전환 권장) / "모호"(follow_up 꼬리질문 권장) / "불충분"(재질문 권장)

[지원 학과]: {department}
[직전 질문]: {last_question}
[지원자 답변]: {answer_text}
"""


def gemini_analyze_pdf(file_path: str) -> dict:
    """PDF 파일 기반 초기 분석 (최초 1회)"""
    client = genai.Client(api_key=GEMINI_API_KEY)
    try:
        raw_text = extract_pdf_text(file_path)
    except Exception as e:
        print(f"      [PDF 추출 실패] {e}")
        raw_text = ""

    if not raw_text.strip():
        print("      [경고] PDF 텍스트가 비어있습니다.")
        return _gemini_fallback()

    prompt = GEMINI_PROMPT_TMPL.format(doc_text=raw_text[:6000])
    gen_config = types.GenerateContentConfig(
        max_output_tokens=1024,
        temperature=0.2,
        response_mime_type="application/json",
        response_schema=GEMINI_RESPONSE_SCHEMA,
    )

    try:
        print("      [Gemini] PDF 텍스트 분석 중...")
        response = client.models.generate_content(
            model="gemini-2.5-flash", contents=prompt, config=gen_config,
        )
        return _parse_gemini_json(response.text.strip()) or _gemini_fallback()
    except Exception as e:
        print(f"      [Gemini 호출 실패] {e}")
        return _gemini_fallback()


def gemini_analyze_answer(department: str, last_question: str, answer_text: str) -> dict:
    """
    ClientMsg 수신 시 지원자 답변을 실시간 분석.
    department / last_question / text 를 직접 받아 키워드·요약·품질 반환.
    """
    client = genai.Client(api_key=GEMINI_API_KEY)
    prompt = GEMINI_ANSWER_TMPL.format(
        department=department or "미지정",
        last_question=last_question or "(첫 질문)",
        answer_text=answer_text or "",
    )
    gen_config = types.GenerateContentConfig(
        max_output_tokens=512,
        temperature=0.2,
        response_mime_type="application/json",
        response_schema=GEMINI_ANSWER_SCHEMA,
    )
    try:
        print("      [Gemini] 답변 분석 중...")
        response = client.models.generate_content(
            model="gemini-2.5-flash", contents=prompt, config=gen_config,
        )
        result = _parse_gemini_json(response.text.strip())
        if result:
            return result
    except Exception as e:
        print(f"      [Gemini 답변 분석 실패] {e}")

    return {"keywords": [], "gemini_summary": answer_text[:100], "answer_quality": "모호"}


def _gemini_fallback() -> dict:
    return {
        "dept": "미분류", "dept_reasoning": "분석 실패",
        "keywords": [], "gemini_summary": "",
    }


def _parse_gemini_json(text: str) -> dict | None:
    try:
        return json.loads(text)
    except json.JSONDecodeError:
        cleaned = re.sub(r"```(?:json)?|```", "", text).strip()
        m = re.search(r"\{.*\}", cleaned, re.DOTALL)
        if m:
            try:
                return json.loads(m.group())
            except json.JSONDecodeError:
                pass
    return None

_model     = None
_tokenizer = None

def load_gemma_lora(adapter_path: str):
    global _model, _tokenizer
    if _model is not None:
        return _model, _tokenizer

    print(f"      베이스 모델 : {BASE_MODEL_ID}")
    print(f"      LoRA 어댑터 : {adapter_path}")

    dtype      = torch.float16 if torch.cuda.is_available() else torch.float32
    device_map = "auto"        if torch.cuda.is_available() else "cpu"

    tokenizer = AutoTokenizer.from_pretrained(BASE_MODEL_ID, use_fast=True, legacy=False)
    tokenizer.pad_token    = tokenizer.eos_token
    tokenizer.padding_side = "left"
    tokenizer.add_special_tokens({"additional_special_tokens": ["<start_of_turn>", "<end_of_turn>"]})

    base  = AutoModelForCausalLM.from_pretrained(BASE_MODEL_ID, dtype=dtype, device_map=device_map)
    model = PeftModel.from_pretrained(base, adapter_path)
    model.eval()

    _model, _tokenizer = model, tokenizer
    print("      [모델 로드 완료]")
    return model, tokenizer

def build_prompt(
    dept: str,
    keywords: list,
    summary: str,
    last_question: str = "",
    answer_text: str   = "",
    answer_quality: str = "모호",
) -> str:
    """
    PDF 기반 초기 질문 OR ClientMsg 기반 꼬리질문 모두 처리.
    last_question / answer_text 가 있으면 맥락으로 삽입.
    answer_quality 에 따라 decision 힌트를 프롬프트에 명시.
    """
    keywords_str = ", ".join(keywords) if isinstance(keywords, list) else str(keywords)
    decision_hint = "next_topic" if answer_quality == "충분" else "follow_up"

    context_block = ""
    if last_question:
        context_block += f"[직전 질문]: {last_question}\n"
    if answer_text:
        context_block += f"[지원자 답변]: {answer_text}\n"
    if context_block:
        context_block = "\n" + context_block

    prompt = (
        f"<start_of_turn>user\n"
        f"당신은 대학 입시 면접관 AI입니다. 아래 정보를 바탕으로 "
        f"지원자의 기술적 깊이와 전공 역량을 검증할 수 있는 "
        f"'날카롭고 구체적인 전공 면접 질문'을 생성하십시오.\n\n"
        f"[학과]: {dept}\n"
        f"[핵심 키워드]: {keywords_str}\n"
        f"[분석 요약]: {summary}"
        f"{context_block}\n"
        f"[권장 decision]: {decision_hint}\n\n"
        f"출력은 반드시 다른 설명 없이 아래와 같은 유효한 JSON 형식 하나만 생성하십시오.\n"
        f'{{"type": "server_content", "content": {{"text": "여기에 핵심 키워드를 반영한 날카로운 질문 작성", '
        f'"decision": "{decision_hint}", '
        f'"emotion": {{"label": "날카룸/압박", "score": 0.8, "intensity": "medium"}}}}}}\n'
        f"<end_of_turn>\n"
        f"<start_of_turn>model\n"
    )
    return prompt

def run_gemma(prompt_text: str, adapter_path: str, max_new_tokens: int = 512) -> tuple[dict, str]:
    model, tokenizer = load_gemma_lora(adapter_path)

    inputs = tokenizer(
        prompt_text, return_tensors="pt", truncation=True, max_length=1024,
    ).to(model.device)

    eos_ids = [tokenizer.eos_token_id]
    eot_id  = tokenizer.convert_tokens_to_ids("<end_of_turn>")
    if eot_id and eot_id != tokenizer.eos_token_id:
        eos_ids.append(eot_id)

    with torch.no_grad():
        outputs = model.generate(
            **inputs,
            max_new_tokens=max_new_tokens,
            do_sample=False,
            temperature=0.7,
            top_p=0.9,
            repetition_penalty=1.1,
            pad_token_id=tokenizer.pad_token_id or tokenizer.eos_token_id,
            eos_token_id=eos_ids,
        )

    generated = outputs[0][inputs["input_ids"].shape[1]:]
    raw_text  = tokenizer.decode(generated, skip_special_tokens=True).strip()

    cleaned    = re.sub(r"```(?:json)?|```", "", raw_text).strip()
    json_match = re.search(r"\{.*\}", cleaned, re.DOTALL)
    if json_match:
        try:
            return json.loads(json_match.group()), raw_text
        except json.JSONDecodeError:
            pass

    # 끊긴 JSON 보정
    if raw_text.startswith("{") and not raw_text.endswith("}"):
        try:
            return json.loads(raw_text + "}"), raw_text
        except json.JSONDecodeError:
            pass

    return {
        "type": "server_content",
        "message_id": str(uuid.uuid4()),
        "content": {
            "text":    raw_text or "질문을 생성하지 못했습니다.",
            "emotion": {"label": "중립/전환", "score": 0.5, "intensity": "medium"},
        },
    }, raw_text

def _emotion_to_action(label: str) -> str:
    for key, action in {
        "날카로움": "avatar_stern",   "압박":   "avatar_stern",
        "정중함":   "avatar_nod",     "마무리": "avatar_nod",
        "중립":     "avatar_neutral", "전환":   "avatar_neutral",
        "격려":     "avatar_smile",   "지지":   "avatar_smile",
        "당혹":     "avatar_tilt",    "재질문": "avatar_tilt",
    }.items():
        if key in label:
            return action
    return "avatar_neutral"

def format_output(raw_dict: dict, gemini_info: dict) -> dict:
    content = raw_dict.get("content", {})
    emotion = content.get("emotion", {})
    return {
        "type":       "server_content",
        "message_id": raw_dict.get("message_id", str(uuid.uuid4())),
        "content": {
            "text":  content.get("text", ""),
            "emotion": {
                "label":     emotion.get("label", "중립/전환"),
                "score":     round(float(emotion.get("score", 0.7)), 2),
                "intensity": emotion.get("intensity", "medium"),
                "action":    _emotion_to_action(emotion.get("label", "")),
            },
        },
        "gemini_analysis": {
            "dept":           gemini_info.get("dept", ""),
            "dept_reasoning": gemini_info.get("dept_reasoning", ""),
            "keywords":       gemini_info.get("keywords", []),
            "summary":        gemini_info.get("gemini_summary", ""),
        },
        "usage": {"timestamp": datetime.now(timezone.utc).isoformat()},
    }

def run_pipeline_from_pdf(file_path: str, adapter_path: str = LORA_ADAPTER_PATH) -> dict:
    print("\n" + "═" * 62)
    print("  [PDF 파이프라인] Gemini → Gemma LoRA")
    print("═" * 62)

    print(f"\n[1/3] Gemini PDF 분석 중... ({Path(file_path).name})")
    gemini_info = gemini_analyze_pdf(file_path)
    dept = gemini_info.get("dept", "미분류")
    print(f"      ▸ 학과   : {dept}")
    print(f"      ▸ 근거   : {gemini_info.get('dept_reasoning', '')}")
    print(f"      ▸ 키워드 : {', '.join(gemini_info.get('keywords', []))}")
    print(f"      ▸ 요약   : {gemini_info.get('gemini_summary', '')}")

    print("\n[2/3] 프롬프트 빌드 중...")
    prompt = build_prompt(
        dept=dept,
        keywords=gemini_info.get("keywords", []),
        summary=gemini_info.get("gemini_summary", ""),
    )

    print("\n[3/3] Gemma 추론 중...")
    result_dict, raw_output = run_gemma(prompt, adapter_path)
    print(f"      Raw: {raw_output[:300]}")

    final = format_output(result_dict, gemini_info)
    print("\n" + "─" * 62)
    print(json.dumps(final, ensure_ascii=False, indent=2))
    print("═" * 62 + "\n")
    return final

def run_pipeline_from_client_msg(
    msg: dict,
    adapter_path: str = LORA_ADAPTER_PATH,
) -> dict:
    """
    클라이언트(Unity 등)가 보낸 ClientMsg dict 를 받아 파이프라인 실행.

    msg 필드:
      type          : "client_msg"
      department    : 지원 학과
      last_question : 직전 면접관 질문
      text          : 지원자 답변
    """
    department    = msg.get("department", "미지정")
    last_question = msg.get("last_question", "")
    answer_text   = msg.get("text", "")

    print("\n" + "═" * 62)
    print("  [ClientMsg 파이프라인]")
    print(f"  학과: {department} | 답변: {answer_text[:60]}...")
    print("═" * 62)

    # 1) Gemini 로 답변 분석 — 키워드·요약·품질 추출
    print("\n[1/3] Gemini 답변 분석 중...")
    answer_analysis = gemini_analyze_answer(department, last_question, answer_text)
    keywords        = answer_analysis.get("keywords", [])
    summary         = answer_analysis.get("gemini_summary", answer_text[:100])
    answer_quality  = answer_analysis.get("answer_quality", "모호")
    print(f"      ▸ 키워드  : {', '.join(keywords)}")
    print(f"      ▸ 요약    : {summary}")
    print(f"      ▸ 답변품질: {answer_quality}")

    # 1. gemini_info 형식으로 통일 (format_output 재사용)
    gemini_info = {
        "dept":           department,
        "dept_reasoning": f"클라이언트 지정 학과",
        "keywords":       keywords,
        "gemini_summary": summary,
    }

    # 2. Gemma 빌드 — last_question / answer_text 맥락 포함
    print("\n[2/3] 프롬프트 빌드 중...")
    prompt = build_prompt(
        dept=department,
        keywords=keywords,
        summary=summary,
        last_question=last_question,
        answer_text=answer_text,
        answer_quality=answer_quality,
    )

    # 3. Gemma 추론
    print("\n[3/3] Gemma 추론 중...")
    result_dict, raw_output = run_gemma(prompt, adapter_path)
    print(f"      Raw: {raw_output[:300]}")

    final = format_output(result_dict, gemini_info)
    print("\n" + "─" * 62)
    print(json.dumps(final, ensure_ascii=False, indent=2))
    print("═" * 62 + "\n")
    return final

def demo_without_file() -> dict:
    print("\n[DEMO MODE] API/모델 없이 출력 구조 확인\n")

    # ClientMsg 형태의 모의 데이터
    mock_client_msg = {
        "type":          "client_msg",
        "department":    "인공지능학과",
        "last_question": "LSTM에서 Cell State가 기울기 소실 문제를 해결하는 원리를 수식 수준으로 설명해보세요.",
        "text":          "Cell State는 게이트를 통해 정보를 선택적으로 기억하거나 삭제합니다. "
                         "forget gate가 σ(W_f·[h,x]+b_f) 형태로 0~1 사이 값을 곱해 이전 상태를 얼마나 유지할지 결정합니다.",
    }

    mock_gemini = {
        "dept":           mock_client_msg["department"],
        "dept_reasoning": "클라이언트 지정 학과",
        "keywords":       ["Cell State", "forget gate", "기울기 소실"],
        "gemini_summary": "LSTM forget gate 원리를 수식으로 설명했으나 input/output gate와의 연계, "
                          "역전파 시 gradient flow 유지 메커니즘 심층 검증 필요.",
    }
    mock_raw = {
        "type":       "server_content",
        "message_id": str(uuid.uuid4()),
        "content": {
            "text":    "forget gate 외에 input gate와 output gate가 역전파 시 gradient 를 어떻게 "
                       "유지시키는지 수식으로 설명해보세요.",
            "emotion": {"label": "날카로움/압박", "score": 0.88, "intensity": "high"},
        },
    }

    print("[DEMO] 수신된 ClientMsg:")
    print(json.dumps(mock_client_msg, ensure_ascii=False, indent=2))
    print()

    final = format_output(mock_raw, mock_gemini)
    print("[DEMO] 생성된 ServerContent:")
    print(json.dumps(final, ensure_ascii=False, indent=2))
    return final

class InterviewWSClient:
    """
    Cloudflare Workers WebSocket 중계서버에 상시 연결 유지.

    동작 흐름:
      1. 연결 후 대기
      2. 클라이언트(Unity)가 ClientMsg 전송 → 중계서버가 Python 에 전달
      3. _on_message 에서 ClientMsg 감지 → 파이프라인 실행 (별도 스레드)
      4. 결과 패킷을 send_packet 으로 중계서버 → 클라이언트 전달
    """

    def __init__(self, session_id: str | None = None, adapter_path: str = LORA_ADAPTER_PATH):
        self.session_id   = session_id or str(uuid.uuid4())
        self.adapter_path = adapter_path
        self.url          = f"{WS_BASE_URL}{WS_ENDPOINT}?session={self.session_id}"
        self._ws: websocket.WebSocketApp | None = None
        self._connected   = threading.Event()
        self._send_queue: list[str] = []
        self._reconnects  = 0
        self._should_stop = False

    def _on_open(self, ws):
        self._connected.set()
        self._reconnects = 0
        print(f"[WS] 연결됨  →  {self.url}")
        print("[WS] ClientMsg 대기 중...\n")
        while self._send_queue:
            ws.send(self._send_queue.pop(0))

    def _on_message(self, ws, message: str):
        """수신 메시지 처리 — ClientMsg 이면 파이프라인 실행"""
        try:
            data = json.loads(message)
        except json.JSONDecodeError:
            print(f"[WS] 수신 (비JSON)  ←  {message[:200]}")
            return

        msg_type = data.get("type", "")
        if msg_type in ("ack", "error", "server_content"):
            print(f"[WS] 수신  ←  type={msg_type}  {json.dumps(data, ensure_ascii=False)[:120]}")
            return
        if msg_type == "client_msg":
            print(f"\n[WS] ClientMsg 수신  ←  dept={data.get('department')}  "
                  f"text={str(data.get('text',''))[:60]}...")
            threading.Thread(
                target=self._handle_client_msg,
                args=(data, data.get("client_session_id")),
                daemon=True,
            ).start()
            return

        print(f"[WS] 알 수 없는 메시지 type={msg_type}")

    def _handle_client_msg(self, msg: dict, client_session_id: str | None = None):
        """별도 스레드에서 파이프라인 실행 후 결과 전송.
        client_session_id 를 결과 패킷에 실어서 Workers 가
        올바른 Unity 클라이언트로 역방향 라우팅할 수 있게 함.
        """
        try:
            result = run_pipeline_from_client_msg(msg, adapter_path=self.adapter_path)
            # Workers 라우팅용 필드 주입
            if client_session_id:
                result["client_session_id"] = client_session_id
            self.send_packet(result)
        except Exception as e:
            print(f"[Pipeline] 오류: {e}")
            error_packet = {
                "type":       "server_content",
                "message_id": str(uuid.uuid4()),
                "content": {
                    "text":    "죄송합니다, 질문 생성 중 오류가 발생했습니다.",
                    "emotion": {"label": "neutral", "score": 0.5, "intensity": "low"},
                },
                "gemini_analysis": {"dept": msg.get("department", ""), "keywords": [], "summary": ""},
                "usage": {"timestamp": datetime.now(timezone.utc).isoformat()},
            }
            if client_session_id:
                error_packet["client_session_id"] = client_session_id
            self.send_packet(error_packet)

    def _on_error(self, ws, error):
        print(f"[WS] 오류: {error}")

    def _on_close(self, ws, close_status_code, close_msg):
        self._connected.clear()
        print(f"[WS] 연결 종료  (code={close_status_code}  msg={close_msg})")
        if not self._should_stop and self._reconnects < MAX_RECONNECT:
            self._reconnects += 1
            print(f"[WS] {RECONNECT_DELAY}초 후 재연결... ({self._reconnects}/{MAX_RECONNECT})")
            time.sleep(RECONNECT_DELAY)
            self._start_ws_thread()

    def _start_ws_thread(self):
        self._ws = websocket.WebSocketApp(
            self.url,
            on_open    = self._on_open,
            on_message = self._on_message,
            on_error   = self._on_error,
            on_close   = self._on_close,
        )
        threading.Thread(
            target=self._ws.run_forever,
            kwargs={"ping_interval": 20, "ping_timeout": 10},
            daemon=True,
        ).start()

    def connect(self, timeout: float = 10.0) -> bool:
        self._should_stop = False
        self._start_ws_thread()
        connected = self._connected.wait(timeout=timeout)
        if not connected:
            print(f"[WS] 연결 타임아웃 ({timeout}s)")
        return connected

    def send_packet(self, packet: dict) -> bool:
        normalized = normalize_packet(packet)
        payload    = json.dumps(normalized, ensure_ascii=False)

        if self._connected.is_set() and self._ws:
            try:
                self._ws.send(payload)
                print(
                    f"[WS] 전송  →  message_id={normalized['message_id']}"
                    f"  emotion={normalized['content']['emotion']['label']}"
                )
                return True
            except Exception as e:
                print(f"[WS] 전송 실패: {e}")

        print("[WS] 연결 대기 중, 큐에 적재...")
        self._send_queue.append(payload)
        return False

    def close(self):
        self._should_stop = True
        if self._ws:
            self._ws.close()
        print("[WS] 연결 종료 요청")

    def wait_forever(self):
        """서버 상시 대기 — Ctrl+C 로 종료"""
        try:
            while not self._should_stop:
                time.sleep(1)
        except KeyboardInterrupt:
            print("\n[WS] 종료 (Ctrl+C)")
            self.close()

if __name__ == "__main__":
    parser = argparse.ArgumentParser(
        description="면접관 AI 서버 — ClientMsg 수신 후 Gemini+Gemma 파이프라인 실행"
    )
    parser.add_argument("--file",       type=str,            help="초기 질문용 PDF (선택)")
    parser.add_argument("--adapter",    type=str,            default=LORA_ADAPTER_PATH)
    parser.add_argument("--gemini-key", type=str,            default="")
    parser.add_argument("--session-id", type=str,            default=None)
    parser.add_argument("--demo",       action="store_true", help="데모 1회 실행 후 종료")
    args = parser.parse_args()

    if args.gemini_key:
        GEMINI_API_KEY = args.gemini_key

    client = InterviewWSClient(session_id=args.session_id, adapter_path=args.adapter)
    print(f"[WS] 세션 ID: {client.session_id}")

    if not client.connect(timeout=10):
        print("[WS] 서버 연결 실패.")
        exit(1)

    # PDF 초기 질문 전송
    if args.file:
        result = run_pipeline_from_pdf(file_path=args.file, adapter_path=args.adapter)
        client.send_packet(result)

    # 데모 모드: mock ClientMsg 1회 처리 후 종료
    if args.demo:
        result = demo_without_file()
        client.send_packet(result)
        time.sleep(2)
        client.close()
    else:
        # 서버 모드: ClientMsg 무한 대기
        print("\n[서버 모드] ClientMsg 대기 중... (종료: Ctrl+C)")
        client.wait_forever()