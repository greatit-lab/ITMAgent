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
            _outputFolder = outputFolder;
            _logManager.LogEvent($"[PdfMergeManager] Output folder updated to: {outputFolder}");
        }

        public void MergeImagesToPdf(List<string> imagePaths, string outputPdfPath)
        {
            if (imagePaths == null || imagePaths.Count == 0)
            {
                _logManager.LogEvent("[PdfMergeManager] No images to merge. Aborting merge process.");
                return;
            }

            try
            {
                string pdfDirectory = IO.Path.GetDirectoryName(outputPdfPath);
                if (!IO.Directory.Exists(pdfDirectory))
                {
                    IO.Directory.CreateDirectory(pdfDirectory);
                    _logManager.LogEvent($"[PdfMergeManager] Created directory: {pdfDirectory}");
                }

                _logManager.LogEvent($"[PdfMergeManager] Starting PDF merge. Output: {IO.Path.GetFileName(outputPdfPath)}, Images: {imagePaths.Count}");

                using (var writer = new PdfWriter(outputPdfPath))
                using (var pdfDoc = new PdfDocument(writer))
                using (var document = new Document(pdfDoc))
                {
                    document.SetMargins(0, 0, 0, 0);

                    for (int i = 0; i < imagePaths.Count; i++)
                    {
                        string imgPath = imagePaths[i];
                        try
                        {
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

                            _logManager.LogDebug($"[PdfMergeManager] Added page {i + 1}: {imgPath} ({w}x{h})");
                        }
                        catch (Exception exImg)
                        {
                            _logManager.LogError($"[PdfMergeManager] Error adding image '{imgPath}': {exImg.Message} | InnerException: {exImg.InnerException?.Message}");
                        }
                    }
                    document.Close();
                }

                _logManager.LogEvent("[PdfMergeManager] PDF creation complete. Starting image file deletion.");
                DeleteMergedImages(imagePaths);
            }
            catch (Exception ex)
            {
                _logManager.LogError($"[PdfMergeManager] A critical error occurred during PDF merge: {ex.Message} | InnerException: {ex.InnerException?.Message}");
                throw;
            }
        }

        private void DeleteMergedImages(List<string> imagePaths)
        {
            int successCount = 0;
            int failCount = 0;
            foreach (string imgPath in imagePaths)
            {
                if (DeleteFileWithRetry(imgPath, maxRetry: 5, delayMs: 300))
                {
                    successCount++;
                    _logManager.LogDebug($"[PdfMergeManager] Deleted image file: {imgPath}");
                }
                else
                {
                    failCount++;
                }
            }
            _logManager.LogEvent($"[PdfMergeManager] Image deletion completed. Success: {successCount}, Failed: {failCount}");
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
                    }
                    return true;
                }
                catch (IO.IOException) when (attempt < maxRetry)
                {
                    Thread.Sleep(delayMs);
                }
                catch (Exception ex)
                {
                    _logManager.LogError($"[PdfMergeManager] Delete attempt {attempt} for {filePath} failed: {ex.Message}");
                    if (attempt >= maxRetry) return false;
                    Thread.Sleep(delayMs);
                }
            }
            return false;
        }
    }
}
