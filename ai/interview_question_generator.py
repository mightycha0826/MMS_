"""
PDF → Gemini(학과 추론 + 요약) → Gemma-2b-it LoRA → 면접 질문 JSON 출력

설치:
  pip install google-genai transformers peft torch accelerate pymupdf sentencepiece

사용법:
  python interview_question_generator.py --file "비터스 AI 부서 6차시-CNN, RNN, LSTM의 핵심 원리와 한계1.pdf" --adapter . --gemini-key "AIzaSyCj8l9-xF2umTBQx6IfAFq_zL7RapeXLfI"
  python interview_test.py --demo
"""

import argparse
import json
import os
import re
import uuid
from datetime import datetime, timezone
from pathlib import Path

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

# 학습 데이터에서 추출한 system prompt — 한 글자도 바꾸지 않음
SYSTEM_PROMPT = (
    "당신은 대학 입시 면접관 AI입니다.\n"
    "Gemini 음성 분석 결과와 직전 면접 맥락을 입력받아,\n"
    "다음 행동을 결정하고 JSON 패킷 하나만 출력하십시오.\n\n"
    "판단 기준:\n"
    "  follow_up  : 답변이 모호하거나 핵심 키워드 검증이 필요한 경우 → 날카로운 꼬리질문\n"
    "  next_topic : 답변이 충분히 구체적이거나 새 섹션으로 이동할 경우 → 자연스러운 전환\n\n"
    "출력은 반드시 유효한 JSON 하나만 생성하십시오. 설명/마크다운 절대 금지."
)


# ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
# 1단계: PDF 텍스트 추출
# ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

def extract_pdf_text(file_path: str) -> str:
    import fitz
    doc = fitz.open(file_path)
    return "\n".join(page.get_text() for page in doc)


# ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
# 2단계: Gemini 분석 — JSON 모드로 강제
# ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

# Gemini response_schema 로 JSON 구조 강제 (마크다운 원천 차단)
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

def gemini_analyze(file_path: str) -> dict:
    client = genai.Client(api_key=GEMINI_API_KEY)

    # PDF 텍스트 추출 + 비어있으면 경고
    try:
        raw_text = extract_pdf_text(file_path)
    except Exception as e:
        print(f"      [PDF 추출 실패] {e}")
        raw_text = ""

    if not raw_text.strip():
        print("      [경고] PDF 텍스트가 비어있습니다. 스캔 이미지 PDF이거나 경로가 잘못됐을 수 있습니다.")
        return _gemini_fallback()

    prompt = GEMINI_PROMPT_TMPL.format(doc_text=raw_text[:6000])

    gen_config = types.GenerateContentConfig(
        max_output_tokens=1024,
        temperature=0.2,
        response_mime_type="application/json",
        response_schema=GEMINI_RESPONSE_SCHEMA,
    )

    response_text = None
    try:
        print("      [Gemini] 텍스트 분석 중...")
        response = client.models.generate_content(
            model="gemini-2.5-flash",
            contents=prompt,
            config=gen_config,
        )
        response_text = response.text.strip()
    except Exception as e:
        print(f"      [Gemini 호출 실패] {e}")
        response_text = None

    if not response_text:
        return _gemini_fallback()

    # JSON 모드여도 안전하게 파싱
    try:
        return json.loads(response_text)
    except json.JSONDecodeError:
        # 혹시 마크다운이 붙은 경우 제거 후 재시도
        cleaned = re.sub(r"```(?:json)?|```", "", response_text).strip()
        m = re.search(r"\{.*\}", cleaned, re.DOTALL)
        if m:
            try:
                return json.loads(m.group())
            except json.JSONDecodeError:
                pass

    print(f"      [Gemini JSON 파싱 실패] 원본:\n      {response_text[:300]}")
    return _gemini_fallback()

def _gemini_fallback() -> dict:
    return {
        "dept":           "미분류",
        "dept_reasoning": "분석 실패",
        "keywords":       [],
        "gemini_summary": "",
    }


# ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
# 3단계: Gemma LoRA 로드 (싱글턴)
# ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

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

    tokenizer = AutoTokenizer.from_pretrained(
        BASE_MODEL_ID,
        use_fast=True,
        legacy=False,
    )
    tokenizer.pad_token = tokenizer.eos_token
    tokenizer.padding_side = "left" 
    
    tokenizer.add_special_tokens({"additional_special_tokens": ["<start_of_turn>", "<end_of_turn>"]})

    base = AutoModelForCausalLM.from_pretrained(
        BASE_MODEL_ID,
        dtype=dtype,
        device_map=device_map,
    )
    model = PeftModel.from_pretrained(base, adapter_path)
    model.eval()

    _model, _tokenizer = model, tokenizer
    print("      [모델 로드 완료]")
    return model, tokenizer


# ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
# 4단계: 프롬프트 빌드 — 학습 데이터 구조 그대로
# ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

def build_prompt(dept: str, keywords: list, summary: str) -> str:
    """
    [수정] Gemini의 핵심 키워드와 요약을 명확히 매핑하여 
    Gemma LoRA 모델이 딥러닝 전공 면접 질문을 생성하도록 유도합니다.
    """
    keywords_str = ", ".join(keywords) if isinstance(keywords, list) else str(keywords)
    
    prompt = (
        f"<start_of_turn>user\n"
        f"당신은 대학 입시 면접관 AI입니다. 아래 제공된 [학과], [핵심 키워드], [문서 요약]을 바탕으로 "
        f"지원자의 기술적 깊이와 전공 역량을 검증할 수 있는 '날카롭고 구체적인 전공 면접 질문'을 생성하십시오.\n\n"
        f"[학과]: {dept}\n"
        f"[핵심 키워드]: {keywords_str}\n"
        f"[문서 요약]: {summary}\n\n"
        f"출력은 반드시 다른 설명 없이 아래와 같은 유효한 JSON 형식 하나만 생성하십시오.\n"
        f'{{"type": "server_content", "content": {{"text": "여기에 핵심 키워드를 반영한 날카로운 질문 작성", "decision": "follow_up", "emotion": {{"label": "날카룸/압박", "score": 0.8, "intensity": "medium"}}}}}}\n'
        f"<end_of_turn>\n"
        f"<start_of_turn>model\n"
    )
    return prompt


# ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
# 5단계: Gemma 추론
# ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

def run_gemma(prompt_text: str, adapter_path: str, max_new_tokens: int = 512) -> tuple[dict, str]:
    model, tokenizer = load_gemma_lora(adapter_path)

    inputs = tokenizer(
        prompt_text,
        return_tensors="pt",
        truncation=True,
        max_length=1024,
    ).to(model.device)

    # completion이 JSON 문자열 그대로 끝남 → eos_token_id 로 종료
    # <end_of_turn> 을 EOS 로 추가해 혹시 모를 이중 종료도 처리
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
            pad_token_id=tokenizer.pad_token_id if tokenizer.pad_token_id is not None else tokenizer.eos_token_id,
            eos_token_id=eos_ids,
            )

    generated = outputs[0][inputs["input_ids"].shape[1]:]
    raw_text  = tokenizer.decode(generated, skip_special_tokens=True).strip()

    # JSON 추출
    cleaned    = re.sub(r"```(?:json)?|```", "", raw_text).strip()
    json_match = re.search(r"\{.*\}", cleaned, re.DOTALL)
    if json_match:
        try:
            return json.loads(json_match.group()), raw_text
        except json.JSONDecodeError:
            pass

    return {
        "type":       "server_content",
        "message_id": str(uuid.uuid4()),
        "content": {
            "text":    raw_text or "질문을 생성하지 못했습니다.",
            "emotion": {"label": "중립/전환", "score": 0.5, "intensity": "medium"},
        },
    }, raw_text


# ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
# 6단계: 출력 패킷 조립
# ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

def _emotion_to_action(label: str) -> str:
    for key, action in {
        "날카로움": "avatar_stern",   "압박":  "avatar_stern",
        "정중함":   "avatar_nod",     "마무리": "avatar_nod",
        "중립":     "avatar_neutral", "전환":  "avatar_neutral",
        "격려":     "avatar_smile",   "지지":  "avatar_smile",
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


# ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
# 메인 파이프라인
# ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

def run_pipeline(file_path: str, adapter_path: str = LORA_ADAPTER_PATH) -> dict:
    print("\n" + "═" * 62)
    print("  Gemini → Gemma LoRA  면접 질문 생성 파이프라인")
    print("═" * 62)

    print(f"\n[1/3] Gemini 분석 중... ({Path(file_path).name})")
    gemini_info = gemini_analyze(file_path)
    dept = gemini_info.get("dept", "미분류")
    print(f"      ▸ 추론된 학과  : {dept}")
    print(f"      ▸ 추론 근거    : {gemini_info.get('dept_reasoning', '')}")
    print(f"      ▸ 핵심 키워드  : {', '.join(gemini_info.get('keywords', []))}")
    print(f"      ▸ 면접관 요약  : {gemini_info.get('gemini_summary', '')}")

    print("\n[2/3] Gemma 프롬프트 빌드 중...")
    # [수정] 제미나이가 분석한 결과에서 keywords와 summary를 추출하여 build_prompt에 전달합니다.
    keywords = gemini_info.get("keywords", [])
    summary = gemini_info.get("summary", "")
    # 인자 개수 불일치 에러 해결 지점
    prompt = build_prompt(dept, keywords, summary)

    print(f"\n[3/3] Gemma-2b-it (LoRA) 추론 중...")
    result_dict, raw_output = run_gemma(prompt, adapter_path)
    print(f"      Raw: {raw_output[:300]}")

    final = format_output(result_dict, gemini_info)

    print("\n" + "─" * 62)
    print("  최종 출력 패킷")
    print("─" * 62)
    print(json.dumps(final, ensure_ascii=False, indent=2))
    print("═" * 62 + "\n")

    return final


# ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
# 데모 모드
# ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

def demo_without_file():
    print("\n[DEMO MODE] API/모델 없이 출력 구조 확인\n")
    mock_gemini = {
        "dept":           "인공지능학과",
        "dept_reasoning": "CNN, RNN, LSTM 등 딥러닝 핵심 개념 전반을 다루는 문서.",
        "keywords":       ["CNN", "RNN", "LSTM"],
        "gemini_summary": "CNN 합성곱 연산, RNN 순환 구조, LSTM 게이트 메커니즘 설명. LSTM Cell State의 기울기 소실 해결 방식 심층 검증 필요.",
    }
    mock_raw = {
        "type":       "server_content",
        "message_id": str(uuid.uuid4()),
        "content": {
            "text":    "LSTM에서 Cell State가 기울기 소실 문제를 해결하는 원리를 수식 수준으로 설명해보세요.",
            "emotion": {"label": "날카로움/압박", "score": 0.88, "intensity": "high"},
        },
    }
    final = format_output(mock_raw, mock_gemini)
    print(json.dumps(final, ensure_ascii=False, indent=2))
    return final


# ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
# CLI
# ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="PDF → Gemini → Gemma LoRA 면접 질문 생성기")
    parser.add_argument("--file",       type=str, help="입력 PDF 파일 경로")
    parser.add_argument("--adapter",    type=str, default=LORA_ADAPTER_PATH, help="LoRA 어댑터 폴더")
    parser.add_argument("--gemini-key", type=str, default="", help="Gemini API 키")
    parser.add_argument("--demo",       action="store_true", help="데모 모드")
    args = parser.parse_args()

    if args.demo:
        demo_without_file()
    else:
        if args.gemini_key:
            GEMINI_API_KEY = args.gemini_key
        if not args.file:
            print("오류: --file 로 PDF 파일을 지정하세요. (또는 --demo)")
            parser.print_help()
            exit(1)
        run_pipeline(file_path=args.file, adapter_path=args.adapter)


        # 5단계: Gemma 추론
        gemma_raw = run_gemma(prompt_text)
        print(f"      Raw: {gemma_raw}")
        
        # 완벽한 JSON이 아니거나 끊겼을 때를 대비한 안전망 파싱
        import json
        try:
            #만약 맨 뒤에 중괄호가 닫히지 않고 끊겼다면 보정
            if gemma_raw.startswith("{") and not gemma_raw.endswith("}"):
                # 대략적으로 괄호가 닫히지 않은 부분까지만 파싱을 시도하거나 예외처리
                pass
            
            res_json = json.loads(gemma_raw)
            final_text = res_json["content"]["text"]
            final_emotion = res_json["content"]["emotion"]
        except Exception:
            # 파싱 실패 시 예외 처리 및 폴백
            final_text = gemma_raw if gemma_raw else "질문을 생성하지 못했습니다."
            final_emotion = {"label": "중립/전환", "score": 0.5, "intensity": "medium", "action": "avatar_neutral"}