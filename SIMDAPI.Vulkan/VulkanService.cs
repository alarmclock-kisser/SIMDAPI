using Silk.NET.Core;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;
using SIMDAPI.DataAccess;

namespace SIMDAPI.Vulkan
{
	public class VulkanService
	{
		public string Repopath => Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "SIMDAPI.Vulkan"));


		public Vk Vk => Vk.GetApi();
		public int Index { get; private set; } = -1;
		public Instance? INST { get; private set; } = null;
		public PhysicalDevice? PHYS { get; private set; } = null;
		public Device? DEV { get; private set; } = null;


		public VulkanRegister? Register { get; private set; } = null;
		public VulkanCompiler? Compiler { get; private set; } = null;
		public VulkanExecutioner? Executioner { get; private set; } = null;


		public List<PhysicalDevice> PhysicalDevices => this.GetPhysicalDevices();
		public List<Device> Devices { get; private set; } = [];



		public VulkanService(int index = -1)
		{
			this.Index = index;

			this.InitializeInstance();
		}



		unsafe
		public void InitializeInstance()
		{
			// Dispose prev Instance
			this.DisposeInstance();

			try
			{
				// Get api
				Vk vk = Vk.GetApi();

				// Application Info
				ApplicationInfo appInfo = new()
				{
					SType = StructureType.ApplicationInfo,
					PApplicationName = (byte*) SilkMarshal.StringToPtr("SIMDAPI"),
					ApplicationVersion = new Version32(1, 0, 0),
					PEngineName = (byte*) SilkMarshal.StringToPtr("SIMDAPIEngine"),
					EngineVersion = new Version32(1, 0, 0),
					ApiVersion = Vk.Version12
				};

				// Instance Create Info
				InstanceCreateInfo instanceCreateInfo = new()
				{
					SType = StructureType.InstanceCreateInfo,
					PApplicationInfo = &appInfo,
					EnabledExtensionCount = 0,
					PpEnabledExtensionNames = null,
					EnabledLayerCount = 0,
					PpEnabledLayerNames = null,
					Flags = 0
				};

				Instance instance;
				if (vk.CreateInstance(&instanceCreateInfo, null, &instance) != Result.Success)
				{
					throw new Exception("Failed to create Vulkan instance.");
				}

				this.INST = instance;

				// Speicher freigeben
				SilkMarshal.Free((nint) appInfo.PApplicationName);
				SilkMarshal.Free((nint) appInfo.PEngineName);

				Console.WriteLine("Vulkan instance created successfully.");
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Error initializing Vulkan instance: {ex.Message}");
			}
		}

		public async void DisposeInstance()
		{
			// Dispose objects
			this.Register?.DisposeAsync();
			this.Register = null;
			this.Compiler?.Dispose();
			this.Compiler = null;
			this.Executioner?.Dispose();
			this.Executioner = null;

			// Null Devices
			this.PHYS = null;
			this.DEV = null;

			// Dispose Instance
			this.INST = null;

			await Task.CompletedTask;
		}


		public unsafe List<PhysicalDevice> GetPhysicalDevices()
		{
			if (this.INST == null)
			{
				Console.WriteLine("Vulkan instance is not initialized. Please initialize the instance first.");
				return [];
			}

			Vk vk = Vk.GetApi();

			// Zähle die verfügbaren Physical Devices
			uint deviceCount = 0;
			vk.EnumeratePhysicalDevices(this.INST.Value, &deviceCount, null);
			if (deviceCount == 0)
			{
				throw new Exception("No Vulkan physical devices found.");
			}

			// Hole die Physical Devices
			PhysicalDevice[] devices = new PhysicalDevice[deviceCount];
			fixed (PhysicalDevice* devicesPtr = devices)
			{
				vk.EnumeratePhysicalDevices(this.INST.Value, &deviceCount, devicesPtr);
			}

			return devices.ToList();
		}

		public unsafe void SelectPhysicalDevice(int index = 0)
		{
			List<PhysicalDevice> devices = GetPhysicalDevices();
			if (index < 0 || index >= devices.Count)
			{
				this.PHYS = null;
				Console.WriteLine($"Invalid physical device index: {index}. No device selected.");
			}

			this.PHYS = devices[index];
			Console.WriteLine($"Physical Device {index} selected.");
		}

		public unsafe void InitializeDevice()
		{
			if (this.PHYS == null)
			{
				throw new InvalidOperationException("Physical device is not selected.");
			}

			Vk vk = this.Vk;

			// Finde eine Queue, die Graphics unterstützt
			uint queueFamilyCount = 0;
			vk.GetPhysicalDeviceQueueFamilyProperties(this.PHYS.Value, &queueFamilyCount, null);
			if (queueFamilyCount == 0)
			{
				throw new Exception("No queue families found.");
			}

			QueueFamilyProperties[] queueFamilies = new QueueFamilyProperties[queueFamilyCount];
			fixed (QueueFamilyProperties* queueFamiliesPtr = queueFamilies)
			{
				vk.GetPhysicalDeviceQueueFamilyProperties(this.PHYS.Value, &queueFamilyCount, queueFamiliesPtr);
			}

			int graphicsQueueFamilyIndex = -1;
			for (int i = 0; i < queueFamilies.Length; i++)
			{
				if ((queueFamilies[i].QueueFlags & QueueFlags.ComputeBit | QueueFlags.GraphicsBit) != 0)
				{
					graphicsQueueFamilyIndex = i;
					break;
				}
			}
			if (graphicsQueueFamilyIndex == -1)
			{
				throw new Exception("No graphics queue family found.");
			}

			float queuePriority = 1.0f;
			DeviceQueueCreateInfo queueCreateInfo = new()
			{
				SType = StructureType.DeviceQueueCreateInfo,
				QueueFamilyIndex = (uint) graphicsQueueFamilyIndex,
				QueueCount = 1,
				PQueuePriorities = &queuePriority
			};

			DeviceCreateInfo deviceCreateInfo = new()
			{
				SType = StructureType.DeviceCreateInfo,
				QueueCreateInfoCount = 1,
				PQueueCreateInfos = &queueCreateInfo,
				EnabledExtensionCount = 0,
				PpEnabledExtensionNames = null,
				EnabledLayerCount = 0,
				PpEnabledLayerNames = null
			};

			Device device;
			Result result = vk.CreateDevice(this.PHYS.Value, &deviceCreateInfo, null, &device);
			if (result != Result.Success)
			{
				throw new Exception($"Failed to create logical device: {result}");
			}

			// Check initialization
			if (this.INST == null || this.PHYS == null || this.DEV == null)
			{
				Console.WriteLine("Vulkan instance, physical device, or logical device is not initialized properly.");
				return;
			}

			// Init. objects
			this.Register = new VulkanRegister(this, this.INST.Value, this.DEV.Value, this.PHYS.Value);
			this.Compiler = new VulkanCompiler(this);
			this.Executioner = new VulkanExecutioner(this);

			this.DEV = device;
			Console.WriteLine("Logical device created successfully.");
		}

		public unsafe void PrintPhysicalDeviceInfo()
		{
			if (this.PHYS == null)
			{
				Console.WriteLine("No physical device selected.");
				return;
			}

			Vk vk = Vk.GetApi();
			PhysicalDeviceProperties props;
			vk.GetPhysicalDeviceProperties(this.PHYS.Value, &props);

			string? deviceName = SilkMarshal.PtrToString((nint) props.DeviceName);
			Console.WriteLine($"Device Name: {deviceName ?? "N/A"}");
			Console.WriteLine($"API Version: {props.ApiVersion}");
			Console.WriteLine($"Driver Version: {props.DriverVersion}");
			Console.WriteLine($"Vendor ID: {props.VendorID}");
			Console.WriteLine($"Device ID: {props.DeviceID}");
			Console.WriteLine($"Device Type: {props.DeviceType}");
		}

		public unsafe string GetPhysicalDeviceName(int index = 0)
		{
			List<PhysicalDevice> devices = this.GetPhysicalDevices();
			if (index < 0 || index >= devices.Count)
			{
				return "Invalid device index.";
			}
			PhysicalDevice device = devices[index];
			Vk vk = Vk.GetApi();
			PhysicalDeviceProperties props;
			vk.GetPhysicalDeviceProperties(device, &props);
			string? deviceName = SilkMarshal.PtrToString((nint) props.DeviceName);

			return deviceName ?? "Unknown Device";
		}

		public unsafe Dictionary<int, string> GetPhysicalDeviceNames()
		{
			List<PhysicalDevice> devices = this.GetPhysicalDevices();
			Dictionary<int, string> deviceNames = [];
			for (int i = 0; i < devices.Count; i++)
			{
				PhysicalDevice device = devices[i];
				Vk vk = Vk.GetApi();
				PhysicalDeviceProperties props;
				vk.GetPhysicalDeviceProperties(device, &props);
				string? deviceName = SilkMarshal.PtrToString((nint) props.DeviceName);
				deviceNames[i] = deviceName ?? "Unknown Device";
			}

			return deviceNames;
		}



		// Accessor methods
		public async Task<IntPtr> MoveImageAsync(ImgObj imgObj)
		{
			if (this.Register == null)
			{
				Console.WriteLine("Vulkan register is not initialized. Please initialize the Vulkan service first.");
				return IntPtr.Zero;
			}
			try
			{
				IntPtr result = await this.Register.MoveImageAsync(imgObj);
				return result;
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Error moving image: {ex.Message}");
				return IntPtr.Zero;
			}
		}
	}
}
