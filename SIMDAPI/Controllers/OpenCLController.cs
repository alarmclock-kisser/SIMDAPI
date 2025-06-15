using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using SIMDAPI.DataAccess;
using SIMDAPI.OpenCL;
using System.Drawing;

namespace SIMDAPI.Controllers
{
	[ApiController]
	[Route("api/[controller]")]
	public class OpenCLController : ControllerBase
	{
		private readonly ImageCollection ImgC;
		private readonly OpenClService OpenCL;
		private readonly ILogger<OpenCLController> Logger;

		public OpenCLController(ImageCollection imgC, OpenClService openCL, ILogger<OpenCLController> logger)
		{
			this.ImgC = imgC;
			this.OpenCL = openCL;
			this.Logger = logger;
		}

		[HttpGet("devices")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public IActionResult GetDevices()
		{
			try
			{
				var names = this.OpenCL.GetNames();
				var result = names.Select((name, index) => new
				{
					Index = index,
					Name = name
				}).ToList();

				return this.Ok(result);
			}
			catch (Exception ex)
			{
				this.Logger.LogError(ex, "GetDevices: Fehler beim Auslesen der verfügbaren OpenCL-Geräte.");
				return this.StatusCode(StatusCodes.Status500InternalServerError, "Fehler beim Auslesen der Geräte.");
			}
		}

		[HttpPost("initialize")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status500InternalServerError)]
		public IActionResult Initialize([FromQuery] int index = -1)
		{
			try
			{
				this.OpenCL.Initialize(index);

				if (this.OpenCL.INDEX < 0 || this.OpenCL.DEV == null)
				{
					this.Logger.LogError("Initialize: OpenCL initialization failed with index {Index}.", index);
					return this.StatusCode(StatusCodes.Status500InternalServerError, "OpenCL konnte nicht initialisiert werden. Bitte überprüfen Sie die Indexnummer oder die OpenCL-Konfiguration.");
				}

				return this.Ok(new
				{
					Message = "OpenCL initialized.",
					SelectedDeviceIndex = this.OpenCL.INDEX,
					DeviceName = this.OpenCL.GetDeviceInfo(),
					PlatformInfo = this.OpenCL.GetPlatformInfo(),
				});
			}
			catch (Exception ex)
			{
				this.Logger.LogError(ex, "Initialize: Failed to initialize OpenCL with device index {Index}.", index);
				return this.StatusCode(StatusCodes.Status500InternalServerError, $"Fehler beim Initialisieren von OpenCL: {ex.Message}");
			}
		}

		[HttpPost("{id}/moveImage")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[ProducesResponseType(StatusCodes.Status500InternalServerError)]
		public async Task<IActionResult> MoveImage(Guid id)
		{
			ImgObj? imgObj = this.ImgC[id];
			if (imgObj == null)
			{
				this.Logger.LogWarning("MoveImage: Image with ID {Id} not found.", id);
				return this.NotFound(new { Message = $"Image with ID {id} not found." });
			}
			try
			{
				await this.OpenCL.MoveImageAsync(imgObj);
				if (!imgObj.OnHost && !imgObj.OnDevice)
				{
					this.Logger.LogError("MoveImage: Image with ID {Id} could not be moved to device. It is neither on Device nor on Host ...", id);
					return this.StatusCode(StatusCodes.Status500InternalServerError, "Das Bild konnte nicht auf das Gerät verschoben werden. Es befindet sich weder auf dem Gerät noch auf dem Host.");
				}

				this.Logger.LogInformation("MoveImage: Image '{Name}' (ID: {Id}) moved successfully.", imgObj.Name, imgObj.Id);
				return this.Ok(new
				{
					imgObj.Id,
					imgObj.Name,
					State = new { imgObj.OnHost, imgObj.OnDevice },
					Pointer = "<" + imgObj.Pointer.ToString("X16") + ">",
					Size = (this.OpenCL.MemoryRegister?.GetBuffer(imgObj.Pointer)?.Size ?? -1) + "bytes on CL-Device"
				});
			}
			catch (Exception ex)
			{
				this.Logger.LogError(ex, "MoveImage: Failed to move image with ID {Id}.", id);
				return this.StatusCode(StatusCodes.Status500InternalServerError, $"Fehler beim Verschieben des Bildes: {ex.Message}");
			}
		}

		[HttpGet("stats")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<IActionResult> GetStats(bool readable = false)
		{
			try
			{
				var reg = this.OpenCL.MemoryRegister;
				if (reg == null)
				{
					this.Logger.LogWarning("GetStats: Memory register is not initialized.");
					return this.StatusCode(StatusCodes.Status503ServiceUnavailable, "OpenCL MemoryRegister ist nicht initialisiert.");
				}

				List<string> desc = ["Total", "Used", "Free"];
				List<long> sizes = await this.OpenCL.GetMemoryStatsAsync(readable);
				Dictionary<string, long> stats = desc.Zip(sizes, (d, s) => new { d, s }).ToDictionary(x => x.d, x => x.s);

				this.Logger.LogInformation("GetStats: OpenCL memory stats retrieved successfully.");
				return this.Ok(new
				{
					Message = "OpenCL memory stats retrieved successfully.",
					Stats = stats,
					Readable = readable
				});
			}
			catch (Exception ex)
			{
				this.Logger.LogError(ex, "GetStats: Fehler beim Abrufen der OpenCL-Statistiken.");
				return this.StatusCode(StatusCodes.Status500InternalServerError, "Fehler beim Abrufen der Statistiken.");
			}
		}



		[HttpPost("ExecuteKernelMandelbrot/{name}/{version}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[ProducesResponseType(StatusCodes.Status500InternalServerError)]
		public async Task<IActionResult> ExecuteKernel([FromQuery] Guid? id = null, string name = "mandelbrotPrecise", string version = "01", double zoom = 1.05D, double xOff = 0.0D, double yOff = 0.0D, int iterCoeff = 32, string hexColor = "#000000")
		{
			ImgObj? imgObj = null;
			if (id == null || id == Guid.Empty)
			{
				// Create new empty image object if no ID is provided
				this.Logger.LogWarning("ExecuteKernel: No image ID provided. Creating a new empty image object.");
				imgObj = this.ImgC.PopEmpty(new SixLabors.ImageSharp.Size(1920, 1080));
				if (imgObj == null)
				{
					this.Logger.LogError("ExecuteKernel: Failed to create a new empty image object.");
					return this.StatusCode(StatusCodes.Status500InternalServerError, "Konnte kein leeres Bildobjekt erstellen. Bitte versuchen Sie es später erneut.");
				}
				this.ImgC.Add(imgObj);
				id = imgObj.Id; // Use the new image ID
			}
			Color col = ColorTranslator.FromHtml(hexColor);
			imgObj = this.ImgC[id.Value];
			if (imgObj == null)
			{
				this.Logger.LogWarning("ExecuteKernel: Image with ID {Id} not found.", id);
				return this.NotFound(new { Message = $"Image with ID {id} not found." });
			}
			try
			{
				// OOP operation (first 4 args are 0 for Pointer & Length (I/O))
				object[] arguments =
					[
						0, 0, 0, 0,
						zoom, xOff, yOff,
						iterCoeff,
						(int) col.R, (int) col.G, (int) col.B
					];

				ImgObj result = await this.OpenCL.ExecuteImageKernelAsync(imgObj, name, version, arguments, true);
				if (result == null || result.Img == null)
				{
					this.Logger.LogError("ExecuteKernel: Image with ID {Id} could not be processed by kernel '{Name}'.", id, name);
					return this.StatusCode(StatusCodes.Status500InternalServerError, "Das Bild konnte nicht durch den Kernel verarbeitet werden. Es ist entweder null oder das Ergebnisbild ist null.");
				}

				this.Logger.LogInformation("ExecuteKernel: Kernel '{Name}' executed successfully for image '{ImageName}' (ID: {Id}).", name, imgObj.Name, id);
				return this.Ok(new
				{
					result.Id,
					result.Name,
					State = new { result.OnHost, result.OnDevice },
					Pointer = "<" + result.Pointer.ToString("X16") + ">",
					Size = (this.OpenCL.MemoryRegister?.GetBuffer(result.Pointer)?.Size ?? -1) + "bytes on CL-Device",
					ResultImage = new
					{
						result.Width,
						result.Height,
						result.Channels,
						result.Bitdepth
					}
				});
			}
			catch (Exception ex)
			{
				this.Logger.LogError(ex, "ExecuteKernel: Failed to execute kernel '{Name}' for image with ID {Id}.", name, id);
				return this.StatusCode(StatusCodes.Status500InternalServerError, $"Fehler bei der Ausführung des Kernels: {ex.Message}");
			}
		}
	}
}