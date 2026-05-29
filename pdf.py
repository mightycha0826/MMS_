import os
from reportlab.lib.pagesizes import letter
from reportlab.platypus import SimpleDocTemplate, Paragraph, Spacer
from reportlab.lib.styles import getSampleStyleSheet, ParagraphStyle
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont

def create_sample_pdf():
    # 저장할 파일명 설정
    file_name = "비터스 AI 부서 6차시-CNN, RNN, LSTM의 핵심 원리와 한계1.pdf"
    
    # PDF 문서 생성 설정 (여백 1인치)
    doc = SimpleDocTemplate(
        file_name,
        pagesize=letter,
        rightMargin=54, leftMargin=54, topMargin=54, bottomMargin=54
    )
    
    # 텍스트 스타일 정의
    styles = getSampleStyleSheet()
    
    # 기본 스타일 기반으로 가독성 높은 한국어 스타일 커스텀 설정
    # (기본 헬베티카 폰트는 영문 전용이므로 안전하게 Helvetica를 기본으로 하되 기본 텍스트 구조 구축)
    title_style = ParagraphStyle(
        'DocTitle',
        parent=styles['Heading1'],
        fontName='Helvetica-Bold',
        fontSize=20,
        leading=24,
        spaceAfter=20,
        textColor='#1A365D' # 짙은 네이비
    )
    
    h1_style = ParagraphStyle(
        'DocH1',
        parent=styles['Heading2'],
        fontName='Helvetica-Bold',
        fontSize=14,
        leading=18,
        spaceBefore=15,
        spaceAfter=10,
        textColor='#2B6CB0' # 파란색 계열
    )

    body_style = ParagraphStyle(
        'DocBody',
        parent=styles['Normal'],
        fontName='Helvetica',
        fontSize=10,
        leading=15,
        spaceAfter=8,
        textColor='#2D3748' # 어두운 회색
    )

    # 본문 시나리오 구성 (Gemini가 추출하여 학과와 키워드를 추론하기 아주 좋은 구성)
    story = []
    
    # 제목
    story.append(Paragraph("<b>[MMS Test Study] AI Department Session 6: Core Architectures &amp; Limitations</b>", title_style))
    story.append(Spacer(1, 15))
    
    # 1. CNN
    story.append(Paragraph("1. Convolutional Neural Networks (CNN) - Spatial Representation", h1_style))
    cnn_text = (
        "CNN architectures are designed to extract spatial features directly from grid-structured inputs "
        "like images. Unlike traditional fully connected layers (Dense Layers) which flatten structural data, "
        "CNN preserves spatial correlation using Local Receptive Fields. The key operations consist of: "
        "(1) Convolutional Filter: sliding learnable kernels to generate feature maps, reducing parameters "
        "via Shared Weights. (2) Pooling Layer: Downsampling feature maps (e.g., Max Pooling) to provide "
        "translation invariance and decrease computational complexity. However, CNNs have limitations in "
        "capturing temporal sequence variations and require significant spatial context tuning."
    )
    story.append(Paragraph(cnn_text, body_style))
    story.append(Spacer(1, 10))
    
    # 2. RNN
    story.append(Paragraph("2. Recurrent Neural Networks (RNN) - Sequential Processing", h1_style))
    rnn_text = (
        "RNN is optimized for sequential data such as natural language and time-series forecasting. "
        "It introduces a feedback loop where the hidden state at time 't' depends on both the input "
        "at time 't' and the hidden state from 't-1'. This allows the model to retain a memory of "
        "previous steps. The critical limitation of standard RNN is the Vanishing and Exploding Gradient "
        "problems during Backpropagation Through Time (BPTT). When dealing with long sequences, gradients "
        "propagated back in time either shrink to zero or grow exponentially, making it impossible to capture "
        "long-term dependencies."
    )
    story.append(Paragraph(rnn_text, body_style))
    story.append(Spacer(1, 10))
    
    # 3. LSTM
    story.append(Paragraph("3. Long Short-Term Memory (LSTM) - Gated Architecture", h1_style))
    lstm_text = (
        "LSTM addresses the vanishing gradient problem of vanilla RNNs by introducing a specialized "
        "gated mechanism and a Cell State (C_t). The Cell State acts as a conveyor belt, allowing "
        "information to flow through with minimal modifications. The flow is regulated by three distinct gates: "
        "(1) Forget Gate: decides what information to discard from the previous cell state. "
        "(2) Input Gate: determines which new information to store in the current cell state. "
        "(3) Output Gate: controls what information from the updated cell state to output as the hidden state. "
        "While highly effective for moderate sequences, LSTM struggles with extreme sequence lengths and "
        "is computationally expensive due to its sequential, non-parallelizable nature."
    )
    story.append(Paragraph(lstm_text, body_style))
    
    # PDF 빌드 실행
    doc.build(story)
    print(f"Successfully created a professional sample PDF: '{file_name}'")

if __name__ == "__main__":
    create_sample_pdf()
