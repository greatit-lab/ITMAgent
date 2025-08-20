// ITM_Agent.Core/PdfMergeManager.cs
using iText.IO.Image;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Element;
using iText.Layout.Properties;
using ITM_Agent.Common.Interfaces;
using System;
using System.Collections.Generic;
using System.Threading;
// 'using System.IO' 대신 별칭(alias)을 사용하여 이름 충돌 해결
using IO = System.IO;
// 'Image' 클래스의 모호성을 해결하기 위해 iText의 Image를 명시적으로 사용
using iTextImage = iText.Layout.Element.Image;


namespace ITM_Agent.Core
{
    /// <summary>
    /// 여러 이미지 파일을 하나의 PDF 파일로 병합하고, 원본 이미지 파일을 삭제하는 기능을 제공하는 서비스 클래스입니다.
    /// </summary>
    public class PdfMergeManager
    {
        private readonly ILogManager _logManager;
        private string _outputFolder;

        public PdfMergeManager(string defaultOutputFolder, ILogManager logger)
        {
            _logManager = logger ?? throw new ArgumentNullException(nameof(logger));

            if (!IO.Directory.Exists(defaultOutputFolder))
            {
                var ex = new IO.DirectoryNotFoundException($"Default output folder does not exist: {defaultOutputFolder}");
                _logManager.LogError($"[PdfMergeManager] Initialization failed. {ex.Message}");
                throw ex;
            }
            _outputFolder = defaultOutputFolder;
            _logManager.LogEvent($"[PdfMergeManager] Initialized with default output folder: {_outputFolder}");
        }

        /// <summary>
        /// PDF 파일이 저장될 출력 폴더 경로를 업데이트합니다.
        /// </summary>
        public void UpdateOutputFolder(string outputFolder)
        {
            if (!IO.Directory.Exists(outputFolder))
            {
                var ex = new IO.DirectoryNotFoundException($"Output folder does not exist: {outputFolder}");
                _logManager.LogError($"[PdfMergeManager] Failed to update output folder. {ex.Message}");
                throw ex;
            }
            _logManager.LogEvent($"[PdfMergeManager] Output folder updated from '{_outputFolder}' to '{outputFolder}'.");
            _outputFolder = outputFolder;
        }

        public void MergeImagesToPdf(List<string> imagePaths, string outputPdfPath)
        {
            if (imagePaths == null || imagePaths.Count == 0)
            {
                _logManager.LogEvent("[PdfMergeManager] No images to merge. Aborting merge process.");
                return;
            }

            _logManager.LogEvent($"[PdfMergeManager] Starting PDF merge for {imagePaths.Count} images. Output file: {IO.Path.GetFileName(outputPdfPath)}");

            try
            {
                string pdfDirectory = IO.Path.GetDirectoryName(outputPdfPath);
                if (!IO.Directory.Exists(pdfDirectory))
                {
                    _logManager.LogDebug($"[PdfMergeManager] Output directory does not exist. Creating directory: {pdfDirectory}");
                    IO.Directory.CreateDirectory(pdfDirectory);
                }

                using (var writer = new PdfWriter(outputPdfPath))
                using (var pdfDoc = new PdfDocument(writer))
                using (var document = new Document(pdfDoc))
                {
                    document.SetMargins(0, 0, 0, 0);

                    for (int i = 0; i < imagePaths.Count; i++)
                    {
                        string imgPath = imagePaths[i];
                        if (!IO.File.Exists(imgPath))
                        {
                            _logManager.LogError($"[PdfMergeManager] Image file not found, skipping: {imgPath}");
                            continue;
                        }

                        try
                        {
                            _logManager.LogDebug($"[PdfMergeManager] Processing image {i + 1}/{imagePaths.Count}: {IO.Path.GetFileName(imgPath)}");
                            byte[] imgBytes = IO.File.ReadAllBytes(imgPath);
                            var imgData = ImageDataFactory.Create(imgBytes);
                            var img = new iTextImage(imgData);
                            float w = img.GetImageWidth();
                            float h = img.GetImageHeight();

                            if (i > 0)
                            {
                                document.Add(new AreaBreak(AreaBreakType.NEXT_PAGE));
                            }

                            pdfDoc.SetDefaultPageSize(new PageSize(w, h));
                            img.SetAutoScale(false);
                            img.SetFixedPosition(0, 0);
                            img.SetWidth(w);
                            img.SetHeight(h);
                            document.Add(img);

                            _logManager.LogDebug($"[PdfMergeManager] Added page {i + 1} from '{IO.Path.GetFileName(imgPath)}' with size {w}x{h}.");
                        }
                        catch (Exception exImg)
                        {
                            _logManager.LogError($"[PdfMergeManager] Error adding image '{imgPath}' to PDF: {exImg.Message} | InnerException: {exImg.InnerException?.Message}");
                            _logManager.LogDebug($"[PdfMergeManager] Image processing exception details: {exImg.ToString()}");
                        }
                    }
                    document.Close();
                }

                _logManager.LogEvent($"[PdfMergeManager] PDF creation complete for '{IO.Path.GetFileName(outputPdfPath)}'. Starting source image file deletion.");
                DeleteMergedImages(imagePaths);
            }
            catch (Exception ex)
            {
                _logManager.LogError($"[PdfMergeManager] A critical error occurred during PDF merge for '{outputPdfPath}': {ex.Message} | InnerException: {ex.InnerException?.Message}");
                _logManager.LogDebug($"[PdfMergeManager] PDF merge exception details: {ex.ToString()}");
                // 병합 실패 시 생성되었을 수 있는 불완전한 PDF 파일 삭제 시도
                if (IO.File.Exists(outputPdfPath))
                {
                    try { IO.File.Delete(outputPdfPath); }
                    catch (Exception delEx) { _logManager.LogError($"[PdfMergeManager] Failed to delete incomplete PDF file '{outputPdfPath}': {delEx.Message}"); }
                }
            }
        }

        private void DeleteMergedImages(List<string> imagePaths)
        {
            int successCount = 0;
            int failCount = 0;
            _logManager.LogDebug($"[PdfMergeManager] Starting deletion of {imagePaths.Count} source images.");
            foreach (string imgPath in imagePaths)
            {
                if (DeleteFileWithRetry(imgPath, maxRetry: 5, delayMs: 300))
                {
                    successCount++;
                }
                else
                {
                    failCount++;
                }
            }
            _logManager.LogEvent($"[PdfMergeManager] Source image deletion completed. Success: {successCount}, Failed: {failCount}");
        }

        private bool DeleteFileWithRetry(string filePath, int maxRetry, int delayMs)
        {
            for (int attempt = 1; attempt <= maxRetry; attempt++)
            {
                try
                {
                    if (IO.File.Exists(filePath))
                    {
                        IO.File.SetAttributes(filePath, IO.FileAttributes.Normal);
                        IO.File.Delete(filePath);
                        _logManager.LogDebug($"[PdfMergeManager] Deleted source image file: {filePath}");
                    }
                    else
                    {
                        _logManager.LogDebug($"[PdfMergeManager] Source image file already deleted, skipping: {filePath}");
                    }
                    return true;
                }
                catch (IO.IOException) when (attempt < maxRetry)
                {
                    _logManager.LogDebug($"[PdfMergeManager] Delete failed for '{filePath}' (IO Exception). Retrying in {delayMs}ms... (Attempt {attempt}/{maxRetry})");
                    Thread.Sleep(delayMs);
                }
                catch (Exception ex)
                {
                    _logManager.LogError($"[PdfMergeManager] Delete attempt {attempt} for '{filePath}' failed critically: {ex.Message}");
                    _logManager.LogDebug($"[PdfMergeManager] File delete exception details: {ex.ToString()}");
                    // 심각한 오류(예: 권한 문제)는 재시도하지 않고 즉시 실패 처리
                    return false;
                }
            }
            _logManager.LogError($"[PdfMergeManager] Failed to delete file '{filePath}' after {maxRetry} retries.");
            return false;
        }
    }
}
