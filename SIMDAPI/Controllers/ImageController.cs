using Microsoft.AspNetCore.Mvc;
using SIMDAPI.DataAccess;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace SIMDAPI.Controllers
{
	[ApiController]
	[Route("api/[controller]")]
	public class ImageController : ControllerBase
	{
		private readonly ImageCollection ImgC;
		private readonly ILogger<ImageController> Logger;

		public ImageController(ImageCollection imgC, ILogger<ImageController> logger)
		{
			this.ImgC = imgC;
			this.Logger = logger;

			Console.WriteLine("### ImageController initialisiert");

		}

		[HttpPost("upload")]
		[Consumes("multipart/form-data")]
		[RequestSizeLimit(32 * 1024 * 1024)]
		public async Task<IActionResult> UploadImage(IFormFile file)
		{
			this.Logger.LogInformation("UploadImage: Empfange Datei {Name}, Größe {Size} Bytes", file.FileName, file.Length);

			if (file == null || file.Length == 0)
			{
				return this.BadRequest("No file uploaded.");
			}

			using MemoryStream memoryStream = new();
			await file.CopyToAsync(memoryStream);
			memoryStream.Position = 0;

			using var image = SixLabors.ImageSharp.Image.Load<Rgba32>(memoryStream);
			if (image == null)
			{
				this.Logger.LogWarning("UploadImage: Failed to load image.");
				return this.BadRequest("Invalid image format.");
			}

			var bytesPerPixel = image.PixelType.BitsPerPixel / 8;
			var rawPixelData = new byte[image.Width * image.Height * bytesPerPixel];
			image.CopyPixelDataTo(rawPixelData);

			var imgObj = new ImgObj(rawPixelData, image.Width, image.Height, file.FileName);

			if (this.ImgC.Add(imgObj))
			{
				this.Logger.LogInformation("UploadImage: Added image {Name} (ID: {Id})", imgObj.Name, imgObj.Id);
				return this.Ok(new
				{
					imgObj.Id,
					imgObj.Name,
					imgObj.Width,
					imgObj.Height,
					imgObj.Channels,
					imgObj.Bitdepth
				});
			}

			return this.BadRequest("Image already exists.");
		}



		[HttpGet("{id}/download")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[ProducesResponseType(StatusCodes.Status500InternalServerError)]
		public async Task<IActionResult> DownloadImage(Guid id, [FromQuery] string format = "png")
		{
			try
			{
				ImgObj? imgObj = this.ImgC[id]; // Access using the indexer
				if (imgObj == null)
				{
					this.Logger.LogWarning("DownloadImage: Image with ID '{Id}' not found.", id);
					return this.NotFound($"Bild mit ID '{id}' nicht gefunden.");
				}

				// Check if the image data is available on the host (server's memory)
				// We need the Img<Rgba32> object to save it to a file format.
				if (!imgObj.OnHost || imgObj.Img == null)
				{
					this.Logger.LogWarning("DownloadImage: Image with ID '{Id}' is not available on host for download (OnHost={OnHost}, Img is null={IsNull}).",
									   id, imgObj.OnHost, imgObj.Img == null);
					return this.BadRequest($"Bild mit ID '{id}' ist nicht auf dem Host verfügbar oder wurde bereits entsorgt. Kann nicht heruntergeladen werden.");
				}

				// Determine the correct ImageSharp encoder and MIME type based on the requested format
				IImageEncoder encoder;
				string contentType;
				string fileExtension;

				switch (format.ToLower())
				{
					case "png":
						encoder = new PngEncoder();
						contentType = "image/png";
						fileExtension = "png";
						break;
					case "jpeg":
					case "jpg": // Also support "jpg" as an alias
						encoder = new JpegEncoder();
						contentType = "image/jpeg";
						fileExtension = "jpeg";
						break;
					case "bmp":
						encoder = new BmpEncoder();
						contentType = "image/bmp";
						fileExtension = "bmp";
						break;
					// Add more formats here if SixLabors.ImageSharp supports them and you need them (e.g., "gif", "tiff")
					default:
						this.Logger.LogWarning("DownloadImage: Unsupported format '{Format}' requested for image ID '{Id}'.", format, id);
						return this.BadRequest("Nicht unterstütztes Bildformat angefordert. Unterstützt: png, jpeg, bmp.");
				}

				// Use the new GetImageAsFileFormatAsync method to get the image bytes
				// This method saves to a MemoryStream internally, avoiding temporary files on the server.
				Byte[] imageBytes = await imgObj.GetImageAsFileFormatAsync(encoder);

				// Return the file content. ASP.NET Core automatically sets the Content-Disposition header
				// to "attachment" when you use File(bytes, contentType, fileName),
				// which tells the browser to download the file and use the provided file name.
				this.Logger.LogInformation("DownloadImage: Successfully prepared image '{Name}' (ID: {Id}) for download as .{FileExtension}.",
									   imgObj.Name, id, fileExtension);
				return this.File(imageBytes, contentType, $"{imgObj.Name}.{fileExtension}");
			}
			catch (Exception ex)
			{
				this.Logger.LogError(ex, "Fehler beim Herunterladen des Bildes mit ID '{Id}'.", id);
				return this.StatusCode(StatusCodes.Status500InternalServerError, $"Interner Serverfehler beim Herunterladen des Bildes mit ID '{id}'.");
			}
		}
	}
}
