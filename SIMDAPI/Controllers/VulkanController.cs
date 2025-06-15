using Microsoft.AspNetCore.Mvc;
using Silk.NET.Vulkan;
using SIMDAPI.DataAccess;
using SIMDAPI.Vulkan;

namespace SIMDAPI.Controllers
{
	[ApiController]
	[Route("api/[controller]")]
	public class VulkanController : ControllerBase
	{
		private readonly ImageCollection ImgC;
		private readonly VulkanService Vulkan;
		private readonly ILogger<VulkanController> Logger;

		public VulkanController(ImageCollection imgC, VulkanService vulkan, ILogger<VulkanController> logger)
		{
			this.ImgC = imgC;
			this.Vulkan = vulkan;
			this.Logger = logger;
		}



		[HttpGet("devices")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public IActionResult GetDevices()
		{
			var devices = this.Vulkan.GetPhysicalDeviceNames();

			if (devices.Count == 0)
			{
				this.Logger.LogWarning("GetDevices: No Vulkan devices found.");
				return this.NotFound("No Vulkan devices found.");
			}

			this.Logger.LogInformation("GetDevices: Found {Count} Vulkan devices.", devices.Count);
			return this.Ok(new
			{
				Message = "Vulkan devices found.",
				Devices = devices
			});
		}

		[HttpPost("initialize")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status500InternalServerError)]
		public IActionResult Initialize([FromQuery] int index = -1)
		{
			try
			{
				this.Vulkan.SelectPhysicalDevice(index);
				this.Vulkan.InitializeDevice();

				this.Logger.LogInformation("Initialize: Vulkan initialized with device index {Index}.", index);

				if (this.Vulkan.PHYS == null)
				{
					return this.NotFound(new
					{
						Message = $"Couldn't initialize vulkan with index {index}."
					});
				}
				else
				{
					return this.Ok(new
					{
						Message = "Vulkan initialized.",
						SelectedDeviceIndex = index,
						PhysicalDeviceName = this.Vulkan.GetPhysicalDeviceName(index)
					});
				}
			}
			catch (Exception ex)
			{
				this.Logger.LogError(ex, "Initialize: Failed to initialize Vulkan with device index {Index}.", index);
				return this.StatusCode(StatusCodes.Status500InternalServerError, $"Fehler beim Initialisieren von Vulkan: {ex.Message}");
			}
		}


		[HttpPost("{id}/move")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[ProducesResponseType(StatusCodes.Status500InternalServerError)]
		public async Task<IActionResult> MoveImage(Guid id)
		{
			ImgObj? imgObj = this.ImgC[id];
			if (imgObj == null)
			{
				this.Logger.LogWarning("MoveImage: Image with ID '{Id}' not found.", id);
				return this.NotFound($"Image with ID '{id}' not found.");
			}

			try
			{
				await this.Vulkan.MoveImageAsync(imgObj);
				this.Logger.LogInformation("MoveImage: Image '{Name}' (ID: {Id}) moved successfully.", imgObj.Name, imgObj.Id);
				return this.Ok(new
				{
					imgObj.Id,
					imgObj.Name,
					State = new { imgObj.OnHost, imgObj.OnDevice }
				});
			}
			catch (Exception ex)
			{
				this.Logger.LogError(ex, "MoveImage: Failed to move image '{Name}' (ID: {Id}).", imgObj.Name, imgObj.Id);
				return this.StatusCode(StatusCodes.Status500InternalServerError, "Error while moving image.");
			}
		}
	}
}
