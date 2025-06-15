using OpenTK;
using OpenTK.Compute.OpenCL;
using SIMDAPI.DataAccess;
using System.Security.Claims;
using System.Text;

namespace SIMDAPI.OpenCL
{
	public class OpenClService
	{
		public string Repopath => Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "OpenCLImaging.CLLayer"));


		public Dictionary<CLDevice, CLPlatform> Devices => this.GetDevices();

		public int INDEX { get; set; } = -1;
		public CLDevice? DEV { get; set; } = null;
		public CLPlatform? PLAT { get; set; } = null;
		public CLContext? CTX { get; set; } = null;


		// Event for UI updates
		public event Action? OnChange;

		public List<string> DevicesComboItems { get; } = [];
		public List<string> PointersListItems { get; } = [];



		public OpenClMemoryRegister? MemoryRegister { get; private set; }
		public OpenClKernelCompiler? KernelCompiler { get; private set; }
		public OpenClKernelExecutioner? KernelExecutioner { get; private set; }



		// Dispose
		public void Dispose(bool silent = false)
		{
			// Dispose context
			if (this.CTX != null)
			{
				CL.ReleaseContext(this.CTX.Value);
				this.PLAT = null;
				this.DEV = null;
				this.CTX = null;
			}

			// Dispose memory handling
			this.MemoryRegister?.Dispose();
			this.MemoryRegister = null; // Clear reference

			// Dispose kernel handling
			this.KernelExecutioner?.Dispose();
			this.KernelExecutioner = null; // Clear reference
			this.KernelCompiler?.Dispose();
			this.KernelCompiler = null; // Clear reference

			// Log
			if (!silent)
			{
				Console.WriteLine("Disposed OpenCL context and resources.");
			}
			OnChange?.Invoke();
		}




		// GET Devices & Platforms
		private CLPlatform[] GetPlatforms()
		{
			CLPlatform[] platforms = [];

			try
			{
				CLResultCode err = CL.GetPlatformIds(out platforms);
				if (err != CLResultCode.Success)
				{
					Console.WriteLine($"Error retrieving OpenCL platforms: {err}");
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Error retrieving OpenCL platforms: {ex.Message}");
			}

			return platforms;
		}

		public Dictionary<CLDevice, CLPlatform> GetDevices()
		{
			Dictionary<CLDevice, CLPlatform> devices = [];

			CLPlatform[] platforms = this.GetPlatforms();
			foreach (CLPlatform platform in platforms)
			{
				try
				{
					CLDevice[] platformDevices = [];
					CLResultCode err = CL.GetDeviceIds(platform, DeviceType.All, out platformDevices);
					if (err != CLResultCode.Success)
					{
						Console.WriteLine($"Error retrieving devices for platform {this.GetPlatformInfo(platform)}: {err}");
						continue;
					}
					foreach (CLDevice device in platformDevices)
					{
						devices.Add(device, platform);
					}
				}
				catch (Exception ex)
				{
					Console.WriteLine($"Error retrieving devices for platform {this.GetPlatformInfo(platform)}: {ex.Message}");
				}
			}
			return devices;
		}
		public Dictionary<string, string> Names => this.GetNames();








		// Device & Platform info
		public string GetDeviceInfo(CLDevice? device = null, DeviceInfo info = DeviceInfo.Name, bool silent = false)
		{
			// Verify device
			device ??= this.DEV;
			if (device == null)
			{
				if (!silent)
				{
					Console.WriteLine("No OpenCL device specified");
				}

				return "N/A";
			}

			// Get device info
			CLResultCode error = CL.GetDeviceInfo(device.Value, info, out byte[] infoBytes);
			if (error != CLResultCode.Success || infoBytes.Length == 0)
			{
				if (!silent)
				{
					Console.WriteLine($"Failed to get device info: {error}");
				}

				return "N/A";
			}

			// Convert to string if T is string
			if (info == DeviceInfo.Name || info == DeviceInfo.DriverVersion || info == DeviceInfo.Version || info == DeviceInfo.Vendor || info == DeviceInfo.Profile || info == DeviceInfo.OpenClCVersion || info == DeviceInfo.Extensions)
			{
				// Handle extensions as comma-separated string
				if (info == DeviceInfo.Extensions)
				{
					string extensions = Encoding.UTF8.GetString(infoBytes).Trim('\0');
					return string.Join(", ", extensions.Split('\0'));
				}
				return Encoding.UTF8.GetString(infoBytes).Trim('\0');
			}

			// Convert to string if T is a numeric type
			if (info == DeviceInfo.MaximumComputeUnits || info == DeviceInfo.MaximumWorkItemDimensions || info == DeviceInfo.MaximumWorkGroupSize || info == DeviceInfo.MaximumClockFrequency || info == DeviceInfo.AddressBits || info == DeviceInfo.VendorId)
			{
				return BitConverter.ToInt32(infoBytes, 0).ToString();
			}
			else if (info == DeviceInfo.MaximumWorkItemSizes)
			{
				return string.Join(", ", infoBytes.Select(b => b.ToString()).ToArray());
			}
			else if (info == DeviceInfo.MaximumConstantBufferSize)
			{
				return BitConverter.ToInt32(infoBytes, 0).ToString();
			}
			else if (info == DeviceInfo.GlobalMemorySize || info == DeviceInfo.LocalMemorySize || info == DeviceInfo.GlobalMemoryCacheSize)
			{
				return BitConverter.ToInt64(infoBytes, 0).ToString();
			}
			else if (info == DeviceInfo.MaximumMemoryAllocationSize)
			{
				return BitConverter.ToUInt64(infoBytes, 0).ToString();
			}
			else if (info == DeviceInfo.MaximumMemoryAllocationSize)
			{
				return BitConverter.ToInt64(infoBytes, 0).ToString();
			}

			// Convert to string if T is a boolean type
			if (info == DeviceInfo.ImageSupport)
			{
				return (infoBytes[0] != 0).ToString();
			}

			// Convert to string if T is a byte array
			// Here you can add more cases if needed

			// Return "N/A" if info type is not supported
			if (!silent)
			{
				Console.WriteLine($"Unsupported device info type: {info}");
			}
			return "N/A";
		}

		public string GetPlatformInfo(CLPlatform? platform = null, PlatformInfo info = PlatformInfo.Name, bool silent = false)
		{
			// Verify platform
			platform ??= this.PLAT;
			if (platform == null)
			{
				if (!silent)
				{
					Console.WriteLine("No OpenCL platform specified");
				}
				return "N/A";
			}

			// Get platform info
			CLResultCode error = CL.GetPlatformInfo(platform.Value, info, out byte[] infoBytes);
			if (error != CLResultCode.Success || infoBytes.Length == 0)
			{
				if (!silent)
				{
					Console.WriteLine($"Failed to get platform info: {error}");
				}
				return "N/A";
			}

			// Convert to string for text-based info types
			if (info == PlatformInfo.Name ||
				info == PlatformInfo.Vendor ||
				info == PlatformInfo.Version ||
				info == PlatformInfo.Profile ||
				info == PlatformInfo.Extensions)
			{
				return Encoding.UTF8.GetString(infoBytes).Trim('\0');
			}

			// Convert numeric types to string
			if (info == PlatformInfo.PlatformHostTimerResolution)
			{
				return BitConverter.ToUInt64(infoBytes, 0).ToString();
			}

			// Handle extension list as comma-separated string
			if (info == PlatformInfo.Extensions)
			{
				string extensions = Encoding.UTF8.GetString(infoBytes).Trim('\0');
				return string.Join(", ", extensions.Split('\0'));
			}

			// Return raw hex for unsupported types
			if (!silent)
			{
				Console.WriteLine($"Unsupported platform info type: {info}");
			}
			return BitConverter.ToString(infoBytes).Replace("-", "");
		}

		public Dictionary<string, string> GetNames()
		{
			// Get all OpenCL devices & platforms
			Dictionary<CLDevice, CLPlatform> devicesPlatforms = this.Devices;

			// Create dictionary for device names and platform names
			Dictionary<string, string> names = [];

			// Iterate over devices
			foreach (CLDevice device in devicesPlatforms.Keys)
			{
				// Get device name
				string deviceName = this.GetDeviceInfo(device, DeviceInfo.Name, true) ?? "N/A";

				// Get platform name
				string platformName = this.GetPlatformInfo(devicesPlatforms[device], PlatformInfo.Name, true) ?? "N/A";

				// Add to dictionary
				names.Add(deviceName, platformName);
			}

			// Return names
			return names;
		}




		// Initialize
		public void Initialize(int index = 0, bool silent = false)
		{
			this.Dispose(true);

			Dictionary<CLDevice, CLPlatform> devicesPlatforms = this.Devices;

			if (index < 0 || index >= devicesPlatforms.Count)
			{
				if (!silent)
				{
					Console.WriteLine("Invalid index for OpenCL device selection");
				}

				OnChange?.Invoke();
				return;
			}

			this.DEV = devicesPlatforms.Keys.ElementAt(index);
			this.PLAT = devicesPlatforms.Values.ElementAt(index);

			this.CTX = CL.CreateContext(0, [this.DEV.Value], 0, IntPtr.Zero, out CLResultCode error);
			if (error != CLResultCode.Success || this.CTX == null)
			{
				if (!silent)
				{
					Console.WriteLine($"Failed to create OpenCL context: {error}");
				}

				OnChange?.Invoke();
				return;
			}
			// Assuming CLCommandQueue is created within OpenClMemoryRegister constructor
			this.MemoryRegister = new OpenClMemoryRegister(this.Repopath, this.CTX.Value, this.DEV.Value, this.PLAT.Value);
			this.KernelCompiler = new OpenClKernelCompiler(this.Repopath, this.MemoryRegister, this.CTX.Value, this.DEV.Value, this.PLAT.Value, this.MemoryRegister.QUE);
			this.KernelExecutioner = new OpenClKernelExecutioner(this.Repopath, this.MemoryRegister, this.CTX.Value, this.DEV.Value, this.PLAT.Value, this.MemoryRegister.QUE, this.KernelCompiler);

			this.INDEX = index;

			if (!silent)
			{
				Console.WriteLine($"Initialized OpenCL context for device {this.GetDeviceInfo(this.DEV, DeviceInfo.Name)} on platform {this.GetPlatformInfo(this.PLAT, PlatformInfo.Name)}");
			}

			OnChange?.Invoke();
		}




		// UI
		public void FillPointers()
		{
			PointersListItems.Clear(); // Clear the list directly

			if (this.MemoryRegister == null)
			{
				Console.WriteLine("Memory register is not initialized.");
				OnChange?.Invoke();
				return;
			}

			foreach (ClMem mem in this.MemoryRegister.Memory.ToList())
			{
				try
				{
					PointersListItems.Add(mem.IndexHandle.ToString("X16") + " - " + mem.ElementType.Name);
				}
				catch (Exception ex)
				{
					Console.WriteLine($"Error adding memory pointer: {ex.Message}");
				}
			}
			OnChange?.Invoke(); // Notify UI
		}



		// Accessible methods
		public async Task<List<long>> GetMemoryStatsAsync(bool readable = false)
		{
			if (this.MemoryRegister == null)
			{
				Console.WriteLine("Memory register is not initialized.");
				return [];
			}

			List<long> sizes = [];
			try
			{
				sizes.Add(await Task.Run(() => this.MemoryRegister.GetMemoryTotal(readable)));
				sizes.Add(await Task.Run(() => this.MemoryRegister.GetMemoryUsed(readable)));
				sizes.Add(await Task.Run(() => this.MemoryRegister.GetMemoryFree(readable)));

				return sizes;
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Error retrieving memory stats: {ex.Message}");
				return [];
			}
		}



		public async Task<IntPtr> MoveImageAsync(ImgObj obj, bool log = false)
		{
			if (this.MemoryRegister == null)
			{
				Console.WriteLine("Memory register is not initialized.");
				return IntPtr.Zero;
			}
			try
			{
				if (obj.OnHost)
				{
					await Task.Run (() =>
					{
						// Move image data to OpenCL memory
						obj.Pointer = this.MemoryRegister.PushImage(obj, log);
					});

					if (log)
					{
						Console.WriteLine($"Image '{obj.Name}' moved to OpenCL memory at pointer {obj.Pointer.ToString("X16")}.");
					}

					return obj.Pointer;
				}
				else if (obj.OnDevice)
				{
					await Task.Run (() =>
					{
						// Move image data from OpenCL memory to host memory
						obj.Pointer = this.MemoryRegister.PullImage(obj);
					});

					if (log)
					{
						Console.WriteLine($"Image '{obj.Name}' moved from OpenCL memory to host memory at pointer {obj.Pointer.ToString("X16")}.");
					}

					return obj.Pointer;
				}
				else
				{
					Console.WriteLine("ImgObj is neither on host nor on device.", "This should not happen, Error!");
					return obj.Pointer;
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Error moving image: {ex.Message}");
				return IntPtr.Zero;
			}
		}

		public ImgObj ExecuteImageKernel(ImgObj obj, string kernelBaseName = "NONE", string kernelVersion = "00", object[]? optionalArgs = null, bool log = false)
		{
			// Check initialized
			if (this.KernelExecutioner == null)
			{
				if (log)
				{
					Console.WriteLine("Kernel executioner not initialized (Cannot execute image kernel)");
				}
				return obj;
			}

			// Verify obj on device
			if (!obj.OnDevice)
			{
				using var _ = this.MoveImageAsync(obj, log);

				if (!obj.OnDevice)
				{
					if (log)
					{
						Console.WriteLine("Image object not on device (Cannot execute image kernel)");
					}

					return obj;
				}
			}

			// Call kernel executioner
			obj.Pointer = this.KernelExecutioner.ExecKernelImage(obj, kernelBaseName, kernelVersion, optionalArgs, log);

			// Check pointer
			if (obj.Pointer == IntPtr.Zero)
			{
				if (log)
				{
					Console.WriteLine("Kernel execution failed (No pointer returned from kernel execution)");
				}
				return obj;
			}

			// Move back
			if (obj.OnDevice)
			{
				using var _ = this.MoveImageAsync(obj, log);
			}

			return obj;
		}

		public async Task<ImgObj> ExecuteImageKernelAsync(ImgObj obj, string kernelBaseName = "NONE", string kernelVersion = "00", object[]? optionalArgs = null, bool log = false)
		{
			// Check initialized
			if (this.KernelExecutioner == null)
			{
				if (log)
				{
					Console.WriteLine("Kernel executioner not initialized (Cannot execute image kernel)");
				}
				return obj;
			}

			// Verify obj on device
			if (!obj.OnDevice)
			{
				await this.MoveImageAsync(obj, log);

				if (!obj.OnDevice)
				{
					if (log)
					{
						Console.WriteLine("Image object not on device (Cannot execute image kernel)");
					}
					return obj;
				}
			}

			// Call kernel executioner asynchronously
			obj.Pointer = await this.KernelExecutioner.ExecKernelImageAsync(obj, kernelBaseName, kernelVersion, optionalArgs, log);

			// Check pointer
			if (obj.Pointer == IntPtr.Zero)
			{
				if (log)
				{
					Console.WriteLine("Kernel execution failed (No pointer returned from kernel execution)");
				}
				return obj;
			}

			// Move back (asynchronously, falls MoveImageAsync existiert)
			if (obj.OnDevice)
			{
				await this.MoveImageAsync(obj, log); // Behält den synchronen Aufruf bei
			}

			return obj;
		}
	}
}
