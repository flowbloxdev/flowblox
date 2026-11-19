# PDF text and OCR

`PDF2TextFlowBlock` continues to emit one dataset per PDF page through its existing `ResultField`.
Existing field names and references remain valid. Additional page fields are `PdfText`, `OcrText`, and `PageNumber`.

- **Use OCR** is off by default. Enable it to recognize pages locally.
- **Pages without PDF text** runs OCR only when PdfPig finds no text on a page.
- **All pages** also recognizes scans on pages containing added PDF text.
- `PdfText` always contains the original PdfPig extraction. `OcrText` contains the recognition result for that page.
- The existing result field selects nonempty OCR text when OCR ran, otherwise the original PDF text.
  It never concatenates the two versions, avoiding duplicated text. Callers can explicitly combine the separate fields.
- A Join Collector converts the page datasets to one document content value.

German and English `tessdata_fast` 4.1.0 language files are bundled under `data/ocr/tessdata`.
Other languages can be supplied through a custom data directory. The default rendering resolution is 300 DPI.
The Tesseract wrapper includes Windows native binaries; other platforms require compatible native Tesseract/Leptonica libraries.
OCR is CPU based and local. No document is sent to an OCR service.

Dependencies: [PdfPig.Rendering.Skia](https://github.com/BobLd/PdfPig.Rendering.Skia),
[Tesseract](https://github.com/tesseract-ocr/tesseract), and [trained data](https://github.com/tesseract-ocr/tessdata_fast/tree/4.1.0)
are distributed under Apache 2.0. Native Leptonica has its own BSD-style license.
The bundled trained-data license is included in `data/ocr`.
