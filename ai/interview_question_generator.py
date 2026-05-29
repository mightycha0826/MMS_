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

GEMINI_API_KEY    = os.environ.get("GEMINI_API_KEY", "YOUR_GEMINI_API_KEY_HERE")
LORA_ADAPTER_PATH = os.environ.get("LORA_ADAPTER_PATH", "./lora_adapter")
BASE_MODEL_ID     = "google/gemma-2b-it"

SYSTEM_PROMPT = (
    "당신은 대학 입시 면접관 AI입니다.\n"
    "Gemini 분석 결과와 직전 면접 맥락을 입력받아,\n"
    "다음 행동을 결정하고 JSON 패킷 하나만 출력하십시오.\n\n"
    "질문 방향 우선순위:\n"
    "  1순위: 지원자가 직접 수행한 탐구·실험·프로젝트·연구에 대한 질문\n"
    "         (예: 어떤 실험을 했는지, 결과가 예상과 달랐던 점, 한계와 개선 방향)\n"
    "  2순위: 탐구 과정에서 활용한 개념·원리에 대한 확인 질문\n"
    "         (원리 설명 자체가 목적이 아니라 탐구와 연결될 때만 사용)\n\n"
    "판단 기준:\n"
    "  follow_up  : 답변이 모호하거나 탐구 경험이 더 필요한 경우 → 날카로운 꼬리질문\n"
    "  next_topic : 답변이 충분히 구체적이거나 새 주제로 이동할 경우 → 자연스러운 전환\n\n"
    "출력은 반드시 유효한 JSON 하나만 생성하십시오. 설명/마크다운 절대 금지."
)

EMOTIONS = {
    "압박":   {"action": "avatar_stern",   "examples": ["날카로움/압박", "압박/재질문"]},
    "호기심": {"action": "avatar_curious", "examples": ["호기심/탐색", "호기심/기대"]},
    "기쁨":   {"action": "avatar_smile",   "examples": ["기쁨/격려", "기쁨/지지"]},
    "당혹":   {"action": "avatar_tilt",    "examples": ["당혹/재질문", "당혹/확인"]},
    "중립":   {"action": "avatar_neutral", "examples": ["중립/전환", "중립/마무리"]},
    "정중":   {"action": "avatar_nod",     "examples": ["정중함/마무리", "정중/전환"]},
}


def extract_pdf_text(file_path: str) -> str:
    import fitz
    doc = fitz.open(file_path)
    return "\n".join(page.get_text() for page in doc)


def extract_pptx_text(file_path: str) -> str:
    from pptx import Presentation
    prs = Presentation(file_path)
    lines = []
    for slide_num, slide in enumerate(prs.slides, 1):
        lines.append(f"[슬라이드 {slide_num}]")
        for shape in slide.shapes:
            if shape.has_text_frame:
                for para in shape.text_frame.paragraphs:
                    text = para.text.strip()
                    if text:
                        lines.append(text)
    return "\n".join(lines)


def extract_text(file_path: str) -> str:
    ext = Path(file_path).suffix.lower()
    if ext == ".pdf":
        return extract_pdf_text(file_path)
    elif ext in (".pptx", ".ppt"):
        return extract_pptx_text(file_path)
    else:
        raise ValueError(f"지원하지 않는 파일 형식: {ext} (pdf, pptx만 가능)")


INIT_PROMPT_TMPL = """당신은 대학 입시 면접 보조 AI입니다.
아래 문서를 분석하고, 반드시 순수 JSON만 출력하십시오.
마크다운/코드블록/설명 절대 금지. 출력의 첫 글자는 반드시 {{ 이어야 합니다.

필드 설명:
- keywords: 면접 질문 소재가 될 구체적 기술 용어 10개 이상 (배열)
- topics: 면접에서 독립적으로 다룰 수 있는 주제 5개 이상. "주제명: 핵심 내용 한 문장" 형식 (배열)
- gemini_summary: 문서 전체 종합 요약. 각 개념의 핵심 원리·상호 관계·취약 포인트 포함. 600자 이내 한국어
- gemma_hint: Gemma 면접관 AI에게 전달할 첫 질문 생성용 힌트. 반드시 "'키워드' 관련 탐구·실험 경험 확인 필요. 직접 수행한 탐구가 있는지 질문." 형식으로 작성. 50자 이내.
- opening_remark: 면접관이 자료를 처음 검토하고 느낀 점을 자연스럽게 표현하는 1문장. 자료에서 인상적이거나 흥미로운 구체적 내용을 언급할 것. 예) "CNN부터 트랜스포머까지 직접 실험해보셨다니 꽤 인상적이네요." / "탐구 주제가 굉장히 독특한데, 어떻게 이 방향으로 잡게 됐는지 궁금하기도 하고요."

출력 형식 예시:
{{"keywords":["키워드1","키워드2"],"topics":["주제1: 설명","주제2: 설명"],"gemini_summary":"요약","gemma_hint":"'합성곱' 키워드 언급했으나 메커니즘 설명 없음. 꼬리질문으로 검증 필요.","opening_remark":"CNN부터 트랜스포머까지 직접 실험해보셨다니 꽤 인상적이네요."}}

문서 내용:
{doc_text}
"""

def gemini_analyze_file(file_path: str) -> dict:
    client = genai.Client(api_key=GEMINI_API_KEY)

    try:
        raw_text = extract_text(file_path)
    except Exception as e:
        print(f"      [파일 추출 실패] {e}")
        raw_text = ""

    if not raw_text.strip():
        print(f"      [경고] 텍스트가 비어있습니다. 파일을 확인해주세요.")
        return {"keywords": [], "gemini_summary": ""}

    prompt = INIT_PROMPT_TMPL.format(doc_text=raw_text[:12000])

    cfg = types.GenerateContentConfig(
        max_output_tokens=4096,
        temperature=0.2,
    )
    import time
    for attempt in range(3):
        try:
            print(f"      [Gemini] 분석 중... (시도 {attempt+1}/3)")
            response = client.models.generate_content(
                model="gemini-2.5-flash",
                contents=prompt,
                config=cfg,
            )
            if response.text is None:
                raise ValueError("response.text is None")
            return _parse_gemini_json(response.text)
        except Exception as e:
            err = str(e)
            if "429" in err and attempt < 2:
                wait = (attempt + 1) * 15
                print(f"      [할당량 초과] {wait}초 후 재시도...")
                time.sleep(wait)
            else:
                print(f"      [Gemini 호출 실패] {e}")
                return None
    return None


EVAL_PROMPT_TMPL = """당신은 대학 입시 면접 보조 AI입니다.
지원자 답변을 평가하고, 반드시 순수 JSON만 출력하십시오.
마크다운/코드블록/설명 절대 금지. 출력의 첫 글자는 반드시 {{ 이어야 합니다.

[핵심 키워드]: {keywords}
[면접 주제 목록]:
{topics}
[문서 요약]: {doc_summary}
[면접관 질문]: {question}
[지원자 답변]: {answer}

판단 기준:
- follow_up: 답변이 모호하거나 깊이가 부족한 경우
- next_topic: 답변이 충분히 구체적인 경우 → 주제 목록에서 아직 안 다룬 주제 선택

감정 선택 기준 (emotionLabel):
- 답변이 훌륭하고 구체적일 때: "기쁨/격려" 또는 "호기심/기대"
- 답변이 흥미로운 방향을 제시할 때: "호기심/탐색"
- 답변이 모호하거나 핵심을 빗나갈 때: "당혹/확인" 또는 "압박/재질문"
- 날카롭게 추가 검증이 필요할 때: "날카로움/압박"
- 새 주제로 자연스럽게 전환할 때: "정중함/마무리" 또는 "중립/전환"
- 예상 밖의 좋은 답변이 나왔을 때: "기쁨/지지"

feedback_comment 작성 기준:
- follow_up인 경우: 어떤 부분이 부족했는지 구체적으로 언급하고, 그래서 어떤 방향으로 다시 물어볼지 자연스럽게 연결. 예) "말씀하신 내용에서 실제 실험 과정이 잘 안 보여서요, 좀 더 구체적으로 여쭤볼게요."
- next_topic인 경우: 답변이 충분했음을 간단히 인정하고 다음 주제로 넘어감을 알림. 예) "네, 충분히 이해했습니다. 그럼 다른 부분으로 넘어가볼게요."
- 훌륭한 답변일 때: 진심 어린 칭찬 한 마디 포함. 예) "오, 그 부분까지 직접 검토하셨군요. 인상적입니다."
- 1~2문장 이내, 자연스러운 면접관 어투

출력 형식 예시:
{{"decision":"follow_up","emotionLabel":"당혹/확인","feedback_comment":"말씀하신 내용에서 실험 결과 해석 부분이 좀 불분명했는데요, 그 부분을 좀 더 여쭤볼게요.","gemini_summary":"'키워드' 키워드 언급했으나 설명 부족. 검증 필요. 또는 다음 주제: 주제명. 핵심 내용. 100자 이내."}}

gemini_summary는 반드시 Gemma 학습 형식에 맞게 작성하십시오:
- follow_up: "'[키워드]' 관련 탐구 경험 언급했으나 [구체성이 부족한 점]. 탐구 과정/결과 꼬리질문 필요."
- next_topic: "'[새 주제]' 관련 직접 수행한 탐구·실험이 있는지 확인 필요."
100자 이내로 작성하십시오.
"""

def gemini_evaluate_answer(
    keywords: list,
    topics: list,
    doc_summary: str,
    question: str,
    answer: str,
) -> dict:
    client = genai.Client(api_key=GEMINI_API_KEY)

    prompt = EVAL_PROMPT_TMPL.format(
        keywords=", ".join(keywords),
        topics="\n".join(f"  - {t}" for t in topics),
        doc_summary=doc_summary,
        question=question,
        answer=answer,
    )

    import time
    cfg = types.GenerateContentConfig(
        max_output_tokens=1024,
        temperature=0.2,
    )
    for attempt in range(3):
        try:
            response = client.models.generate_content(
                model="gemini-2.5-flash",
                contents=prompt,
                config=cfg,
            )
            if response.text is None:
                raise ValueError("response.text is None")
            result = _parse_gemini_json(response.text)
            if result.get("decision") not in ("follow_up", "next_topic"):
                result["decision"] = "follow_up"
            return result
        except Exception as e:
            err = str(e)
            if "429" in err and attempt < 2:
                wait = (attempt + 1) * 15
                print(f"      [할당량 초과] {wait}초 후 재시도...")
                time.sleep(wait)
            else:
                print(f"      [Gemini 평가 실패] {e}")
                return {"decision": "follow_up", "emotionLabel": "중립/전환", "feedback_comment": "", "gemini_summary": doc_summary}
    return {"decision": "follow_up", "emotionLabel": "중립/전환", "feedback_comment": "", "gemini_summary": doc_summary}


def _parse_gemini_json(text: str) -> dict:
    text = text.strip()

    try:
        return json.loads(text)
    except json.JSONDecodeError:
        pass

    cleaned = re.sub(r"```(?:json)?\s*", "", text)
    cleaned = re.sub(r"```", "", cleaned).strip()
    try:
        return json.loads(cleaned)
    except json.JSONDecodeError:
        pass

    start = text.find("{")
    end   = text.rfind("}")
    if start != -1 and end != -1 and end > start:
        try:
            return json.loads(text[start:end + 1])
        except json.JSONDecodeError:
            pass

    print(f"      [Gemini JSON 파싱 실패] 원본: {text[:200]}")
    return {}


REFINE_PROMPT_TMPL = """당신은 대학 입시 면접 질문 검수 AI입니다.
아래 지시를 따르고, 출력은 반드시 JSON 객체 하나만 작성하십시오.
{{ 로 시작해서 }} 로 끝나야 합니다.
"Here is", "```", 설명문, 마크다운 등 JSON 외 어떤 텍스트도 절대 출력하지 마십시오.

[PDF 핵심 키워드]: {keywords}
[면접 주제 목록]:
{topics}
[Gemma 초안 질문]: {draft_question}
[자료 첫인상 멘트 (있을 경우 질문 앞에 자연스럽게 붙일 것)]: {opening_remark}

검수 규칙:
0. 질문 텍스트 정제 (항상 적용, 해당 표현이 있으면 반드시 modified: true):
   - "(웃으면서)", "(미소지으며)", "(끄덕이며)" 등 괄호 안 행동 묘사 → 모두 제거
   - "시간이 다 돼서~", "그럼~", "음~", "자~" 등 불필요한 도입 문구 → 제거
   - "이거 말고", "또 다른 질문" 등 맥락 없는 메타 발언 → 제거
   - 정제 후 질문 핵심만 남기고 자연스럽게 다듬을 것
1. opening_remark가 있으면 질문 앞에 자연스럽게 붙여서 question 필드에 포함시킬 것 (modified: true)
   - 예) "탐구 주제가 굉장히 독특하네요. 직접 이 실험을 설계하게 된 계기가 뭔가요?"
   - 멘트와 질문 사이 흐름이 어색하지 않게 연결할 것
2. 정제 후 질문이 위 키워드/주제와 관련 있고 탐구·경험·연구를 묻는 방향이면 → modified: true (정제만 적용)
3. 정제 후에도 키워드/주제와 무관하거나 단순 원리 설명만 요구하면
   → 어투·길이·압박 강도는 유지하되, 탐구·실험·경험을 묻는 방향으로 추가 교정 (modified: true)
4. 절대로 완전히 새로운 질문을 창작하지 마십시오. 초안을 기반으로만 수정하십시오.

출력 예시 (이 형식 그대로, 다른 텍스트 없이):
{{"modified": false, "question": "초안 질문 그대로"}}
{{"modified": true, "question": "수정된 질문"}}
"""

_fallback_topic_idx = 0

def _make_fallback_question(keywords: list, topics: list) -> str:
    global _fallback_topic_idx
    if topics:
        topic = topics[_fallback_topic_idx % len(topics)]
        _fallback_topic_idx += 1
        topic_name = topic.split(":")[0].strip()
        return f"{topic_name}에 대해 구체적으로 설명해보세요."
    if keywords:
        kw = keywords[_fallback_topic_idx % len(keywords)]
        _fallback_topic_idx += 1
        return f"{kw}의 핵심 원리에 대해 설명해보세요."
    return "방금 답변하신 내용을 좀 더 구체적으로 설명해주시겠어요?"


def gemini_refine_question(
    keywords: list,
    topics: list,
    draft_question: str,
    opening_remark: str = "",
) -> str:
    if not draft_question or draft_question == "질문을 생성하지 못했습니다.":
        return _make_fallback_question(keywords, topics)

    client = genai.Client(api_key=GEMINI_API_KEY)

    topic_str = "\n".join(f"  - {t}" for t in topics[:5])
    kw_str    = ", ".join(keywords[:10])

    prompt = REFINE_PROMPT_TMPL.format(
        keywords=kw_str,
        topics=topic_str,
        draft_question=draft_question,
        opening_remark=opening_remark,
    )

    import time
    cfg = types.GenerateContentConfig(
        max_output_tokens=2048,
        temperature=0.1,
    )
    for attempt in range(3):
        try:
            response = client.models.generate_content(
                model="gemini-2.5-flash",
                contents=prompt,
                config=cfg,
            )
            if response.text is None:
                print(f"      [Gemini 검수 실패] response.text=None — fallback 사용")
                return _make_fallback_question(keywords, topics)

            raw    = response.text.strip()
            result = _parse_gemini_json(raw)

            if result:
                refined      = result.get("question", "").strip()
                was_modified = result.get("modified", False)

                if not refined:
                    return _make_fallback_question(keywords, topics)

                if was_modified:
                    print(f"      [Gemini 수정] 초안: \"{draft_question}\"")
                    print(f"      [Gemini 수정] 수정: \"{refined}\"")
                else:
                    print(f"      [Gemini 검수] 초안 통과 (수정 없음)")

                return refined

            print(f"      [Gemini 검수 파싱 실패] 원본 앞 100자: {raw[:100]!r}")
            fallback = _make_fallback_question(keywords, topics)
            print(f"      [Gemini 검수 실패] topics fallback 사용: {fallback!r}")
            return fallback

        except Exception as e:
            err = str(e)
            if "429" in err and attempt < 2:
                wait = (attempt + 1) * 15
                print(f"      [할당량 초과] {wait}초 후 재시도...")
                time.sleep(wait)
            else:
                print(f"      [Gemini 검수 실패] {e} — fallback 사용")
                return _make_fallback_question(keywords, topics)

    return _make_fallback_question(keywords, topics)


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
    tokenizer.pad_token      = tokenizer.eos_token
    tokenizer.padding_side   = "left"
    tokenizer.add_special_tokens({"additional_special_tokens": ["<start_of_turn>", "<end_of_turn>"]})

    base  = AutoModelForCausalLM.from_pretrained(BASE_MODEL_ID, dtype=dtype, device_map=device_map)
    model = PeftModel.from_pretrained(base, adapter_path)
    model.eval()

    _model, _tokenizer = model, tokenizer
    print("      [모델 로드 완료]")
    return model, tokenizer


def build_prompt(
    prev_question: str,
    gemini_summary: str,
    keywords: list = None,
    topics: list = None,
) -> str:
    summary_short = gemini_summary[:150].rstrip()

    kw_str    = ", ".join((keywords or [])[:8])
    topic_str = "\n".join(f"  - {t}" for t in (topics or [])[:3])

    user_content = (
        f"핵심 키워드: {kw_str}\n"
        f"면접 주제:\n{topic_str}\n"
        f"직전 면접관 질문: {prev_question}\n"
        f"Gemini 분석 결과: {summary_short}"
    )
    return (
        f"<start_of_turn>system\n{SYSTEM_PROMPT}<end_of_turn>\n"
        f"<start_of_turn>user\n{user_content}<end_of_turn>\n"
        f"<start_of_turn>model\n"
    )


def run_gemma(prompt_text: str, adapter_path: str, max_new_tokens: int = 300) -> tuple[dict, str]:
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
            do_sample=True,
            temperature=0.7,
            top_p=0.9,
            repetition_penalty=1.1,
            pad_token_id=tokenizer.pad_token_id,
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

    first_line = raw_text.split("\n")[0].strip()
    fallback_q = first_line if first_line else "답변 내용에 대해 더 구체적으로 설명해보세요."
    return {
        "type": "server_content", "message_id": str(uuid.uuid4()),
        "content": {
            "text":    fallback_q,
            "emotion": {"label": "중립/전환", "score": 0.5, "intensity": "medium"},
        },
    }, raw_text


def _emotion_label_to_action(label: str) -> str:
    for key, data in EMOTIONS.items():
        if key in label:
            return data["action"]
    return "avatar_neutral"


def format_output(
    raw_dict: dict,
    decision: str,
    emotion_label: str,
    feedback_comment: str,
    final_question: str,
) -> dict:
    return {
        "type":       "server_content",
        "message_id": raw_dict.get("message_id", str(uuid.uuid4())),
        "content": {
            "text":           feedback_comment + " " + final_question if feedback_comment else final_question,
            "question":       final_question,
            "feedbackComment": feedback_comment,
            "decision":       decision,
            "emotionLabel":   emotion_label,
        },
    }


def run_pipeline(file_path: str, adapter_path: str = LORA_ADAPTER_PATH):
    print("\n" + "═" * 62)
    print("  Gemini → Gemma LoRA  면접 시뮬레이터")
    print("  종료: q 입력 후 Enter")
    print("═" * 62)

    print(f"\n[초기 분석] 파일 읽는 중... ({Path(file_path).name})")
    gemini_info = gemini_analyze_file(file_path)
    if gemini_info is None:
        print("\n[오류] 분석 실패. API 키 할당량을 확인하거나 잠시 후 다시 시도하세요.")
        return

    keywords    = gemini_info.get("keywords", [])
    topics      = gemini_info.get("topics", [])
    doc_summary = gemini_info.get("gemini_summary", "")
    opening_remark = gemini_info.get("opening_remark", "")

    print(f"  ▸ 키워드  : {', '.join(keywords)}")
    print(f"  ▸ 주제 목록:")
    for t in topics:
        print(f"      - {t}")
    print(f"  ▸ 요약    : {doc_summary}")

    print(f"\n[모델 로드] Gemma-2b-it + LoRA 로딩 중...")
    load_gemma_lora(adapter_path)

    print(f"\n[질문 생성] 첫 번째 질문 생성 중...")
    gemma_hint = gemini_info.get("gemma_hint", "")
    if not gemma_hint and keywords:
        gemma_hint = f"'{keywords[0]}' 키워드 언급했으나 메커니즘 설명 없음. 꼬리질문으로 검증 필요."

    prompt = build_prompt(
        prev_question="",
        gemini_summary=gemma_hint,
        keywords=keywords,
        topics=topics,
    )
    result_dict, raw = run_gemma(prompt, adapter_path)

    print(f"\n[질문 검수] Gemini 검수 중...")
    draft_q = result_dict.get("content", {}).get("text", raw)
    final_q = gemini_refine_question(keywords, topics, draft_q, opening_remark=opening_remark)

    packet = format_output(
        raw_dict=result_dict,
        decision="follow_up",
        emotion_label="호기심/탐색",
        feedback_comment="",
        final_question=final_q,
    )

    turn             = 1
    current_summary  = doc_summary
    current_question = packet["content"]["question"]

    while True:
        print("\n" + "─" * 62)
        print(f"  [Q{turn}] {packet['content']['text']}")
        print("─" * 62)
        print(json.dumps(packet, ensure_ascii=False, indent=2))
        print()

        try:
            answer = input("▶ 답변 입력 (종료: q): ").strip()
        except (EOFError, KeyboardInterrupt):
            print("\n[면접 종료]")
            break

        if answer.lower() == "q":
            print("\n[면접 종료]")
            break

        if not answer:
            print("  (답변이 없습니다. 다시 입력해주세요.)")
            continue

        print(f"\n[Gemini 평가] 답변 분석 중...")
        eval_result = gemini_evaluate_answer(
            keywords=keywords,
            topics=topics,
            doc_summary=doc_summary,
            question=current_question,
            answer=answer,
        )
        decision         = eval_result.get("decision", "follow_up")
        emotion_label    = eval_result.get("emotionLabel", "중립/전환")
        feedback_comment = eval_result.get("feedback_comment", "")
        current_summary  = eval_result.get("gemini_summary", current_summary)

        label = "꼬리질문" if decision == "follow_up" else "새 주제"
        print(f"  ▸ 판단    : {decision}  ({label})")
        print(f"  ▸ 감정    : {emotion_label}")
        print(f"  ▸ 피드백  : {feedback_comment}")
        print(f"  ▸ 새 요약 : {current_summary}")

        turn += 1
        print(f"\n[질문 생성] Q{turn} 생성 중...")

        prompt = build_prompt(
            prev_question=current_question,
            gemini_summary=current_summary,
            keywords=keywords,
            topics=topics,
        )
        result_dict, raw = run_gemma(prompt, adapter_path)

        print(f"\n[질문 검수] Gemini 검수 중...")
        draft_q = result_dict.get("content", {}).get("text", raw)
        final_q = gemini_refine_question(keywords, topics, draft_q)

        packet = format_output(
            raw_dict=result_dict,
            decision=decision,
            emotion_label=emotion_label,
            feedback_comment=feedback_comment,
            final_question=final_q,
        )
        current_question = packet["content"]["question"]


def demo_without_file():
    print("\n[DEMO MODE] 구조 확인용 — API/모델 호출 없음\n")

    mock_q1_packet = {
        "type":       "server_content",
        "message_id": str(uuid.uuid4()),
        "content": {
            "text":           "CNN부터 트랜스포머까지 직접 실험해보셨다니 꽤 인상적이네요. LSTM에서 Cell State가 기울기 소실 문제를 해결하는 원리를 직접 탐구하거나 실험해본 경험이 있다면 말씀해주세요.",
            "question":       "LSTM에서 Cell State가 기울기 소실 문제를 해결하는 원리를 직접 탐구하거나 실험해본 경험이 있다면 말씀해주세요.",
            "feedbackComment": "",
            "decision":       "follow_up",
            "emotionLabel":   "호기심/탐색",
        },
    }
    print("[Q1]")
    print(json.dumps(mock_q1_packet, ensure_ascii=False, indent=2))

    print("\n--- 가상 답변: 'forget gate가 이전 상태를 지웁니다' ---")

    mock_q2_packet = {
        "type":       "server_content",
        "message_id": str(uuid.uuid4()),
        "content": {
            "text":           "forget gate 언급은 좋았는데, 실제로 수식 수준에서 어떻게 작동하는지는 조금 불분명했어요. 그 부분을 좀 더 여쭤볼게요. 구체적으로 forget gate의 연산 과정을 직접 구현하거나 분석해보신 적 있나요?",
            "question":       "구체적으로 forget gate의 연산 과정을 직접 구현하거나 분석해보신 적 있나요?",
            "feedbackComment": "forget gate 언급은 좋았는데, 실제로 수식 수준에서 어떻게 작동하는지는 조금 불분명했어요. 그 부분을 좀 더 여쭤볼게요.",
            "decision":       "follow_up",
            "emotionLabel":   "당혹/확인",
        },
    }
    print("\n[Q2]")
    print(json.dumps(mock_q2_packet, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="PDF → Gemini → Gemma LoRA 면접 시뮬레이터")
    parser.add_argument("--file",       type=str, help="입력 파일 경로 (PDF 또는 PPTX)")
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
            print("오류: --file 로 PDF 또는 PPTX 파일을 지정하세요. (또는 --demo)")
            parser.print_help()
            exit(1)
        run_pipeline(file_path=args.file, adapter_path=args.adapter)