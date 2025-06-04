// namespace MyTts.Controllers
// {
//     using System;
//     using System.Collections.Generic;
//     using System.IO;
//     using System.Threading.Tasks;
//     using FFMpegCore;
//     using FFMpegCore.Pipes;
//     using Microsoft.AspNetCore.Mvc;
//     using Microsoft.AspNetCore.Http;
//     using System.Linq;
//     using Microsoft.Net.Http.Headers;

//     namespace MyTts.Controllers
//     {
//         /// <summary>
//         /// Controller for handling MP3 file merging and streaming.
//         /// </summary>
//         [ApiController]
//         [Route("api/[controller]")]
//         public class FullStreamingMp3Controller : ControllerBase
//         {
//             [HttpPost("merge-stream")]
//             public async Task MergeMp3FilesStream([FromBody] List<string> fileIds)
//             {
//                 var tempFiles = new List<string>();

//                 try
//                 {
//                     // Retrieve MP3 data and write to temporary files
//                     foreach (var id in fileIds)
//                     {
//                         // Replace with your actual storage retrieval logic
//                         string filePath = Path.Combine("YourStorageDirectory", id + ".mp3");

//                         if (!System.IO.File.Exists(filePath))
//                         {
//                             Response.StatusCode = 400;
//                             await Response.WriteAsync($"File with ID '{id}' not found");
//                             return;
//                         }

//                         // Instead of loading into memory, just use the file path directly
//                         tempFiles.Add(filePath);
//                     }

//                     if (tempFiles.Count == 0)
//                     {
//                         Response.StatusCode = 400;
//                         await Response.WriteAsync("No MP3 data provided");
//                         return;
//                     }

//                     if (tempFiles.Count == 1)
//                     {
//                         // Just stream the single file directly
//                         Response.StatusCode = 200;
//                         Response.ContentType = "audio/mpeg";
//                         Response.Headers[HeaderNames.ContentDisposition] =
//                             new ContentDispositionHeaderValue("attachment") { FileName = "merged.mp3" }.ToString();

//                         using var fileStream = System.IO.File.OpenRead(tempFiles[0]);
//                         await fileStream.CopyToAsync(Response.Body);
//                         return;
//                     }

//                     // Create a text file with the list of files to concatenate
//                     string listFilePath = Path.GetTempFileName() + ".txt";
//                     using (StreamWriter writer = new StreamWriter(listFilePath))
//                     {
//                         foreach (string file in tempFiles)
//                         {
//                             writer.WriteLine($"file '{file.Replace("\\", "/")}'");
//                         }
//                     }

//                     // Set up streaming response
//                     Response.StatusCode = 200;
//                     Response.ContentType = "audio/mpeg";
//                     Response.Headers[HeaderNames.ContentDisposition] =
//                         new ContentDispositionHeaderValue("attachment") { FileName = "merged.mp3" }.ToString();

//                     // Stream directly to the HTTP response
//                     await FFMpegArguments
//                         .FromFileInput(listFilePath)
//                         .OutputToPipe(new StreamPipeSink(Response.Body), options => options
//                             .WithCustomArgument("-f concat")
//                             .WithCustomArgument("-safe 0")
//                             .WithAudioCodec("copy")
//                             .ForceFormat("mp3"))
//                         .ProcessAsynchronously();

//                     // Clean up temporary files
//                     System.IO.File.Delete(listFilePath);
//                 }
//                 catch (Exception ex)
//                 {
//                     // Only attempt to write error if headers haven't been sent
//                     if (!Response.HasStarted)
//                     {
//                         Response.StatusCode = 500;
//                         await Response.WriteAsync($"Error merging MP3 files: {ex.Message}");
//                     }
//                 }
//             }

//             [HttpPost("merge-uploads-stream")]
//             public async Task MergeMp3UploadsStream(List<IFormFile> files)
//             {
//                 var tempFiles = new List<string>();

//                 try
//                 {
//                     if (files == null || !files.Any())
//                     {
//                         Response.StatusCode = 400;
//                         await Response.WriteAsync("No files uploaded");
//                         return;
//                     }

//                     // Save uploaded files to temporary location
//                     foreach (var file in files)
//                     {
//                         if (file.Length > 0)
//                         {
//                             string tempFile = Path.GetTempFileName() + ".mp3";
//                             using (var fileStream = new FileStream(tempFile, FileMode.Create))
//                             {
//                                 await file.CopyToAsync(fileStream);
//                             }
//                             tempFiles.Add(tempFile);
//                         }
//                     }

//                     if (tempFiles.Count == 0)
//                     {
//                         Response.StatusCode = 400;
//                         await Response.WriteAsync("No valid MP3 data found in uploads");
//                         return;
//                     }

//                     if (tempFiles.Count == 1)
//                     {
//                         // Just stream the single file directly
//                         Response.StatusCode = 200;
//                         Response.ContentType = "audio/mpeg";
//                         Response.Headers[HeaderNames.ContentDisposition] =
//                             new ContentDispositionHeaderValue("attachment") { FileName = "merged.mp3" }.ToString();

//                         using var fileStream = System.IO.File.OpenRead(tempFiles[0]);
//                         await fileStream.CopyToAsync(Response.Body);

//                         // Clean up
//                         System.IO.File.Delete(tempFiles[0]);
//                         return;
//                     }

//                     // Create a text file with the list of files to concatenate
//                     string listFilePath = Path.GetTempFileName() + ".txt";
//                     using (StreamWriter writer = new StreamWriter(listFilePath))
//                     {
//                         foreach (string file in tempFiles)
//                         {
//                             writer.WriteLine($"file '{file.Replace("\\", "/")}'");
//                         }
//                     }

//                     // Set up streaming response
//                     Response.StatusCode = 200;
//                     Response.ContentType = "audio/mpeg";
//                     Response.Headers[HeaderNames.ContentDisposition] =
//                         new ContentDispositionHeaderValue("attachment") { FileName = "merged.mp3" }.ToString();

//                     // Stream directly to the HTTP response
//                     await FFMpegArguments
//                         .FromFileInput(listFilePath)
//                         .OutputToPipe(new StreamPipeSink(Response.Body), options => options
//                             .WithCustomArgument("-f concat")
//                             .WithCustomArgument("-safe 0")
//                             .WithAudioCodec("copy")
//                             .ForceFormat("mp3"))
//                         .ProcessAsynchronously();

//                     // Clean up temporary files
//                     System.IO.File.Delete(listFilePath);
//                     foreach (string file in tempFiles)
//                     {
//                         System.IO.File.Delete(file);
//                     }
//                 }
//                 catch (Exception ex)
//                 {
//                     // Clean up any temporary files in case of errors
//                     foreach (string file in tempFiles)
//                     {
//                         if (System.IO.File.Exists(file))
//                             System.IO.File.Delete(file);
//                     }

//                     // Only attempt to write error if headers haven't been sent
//                     if (!Response.HasStarted)
//                     {
//                         Response.StatusCode = 500;
//                         await Response.WriteAsync($"Error merging MP3 files: {ex.Message}");
//                     }
//                 }
//             }
//         }
//     }
// }


