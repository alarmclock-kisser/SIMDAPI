using Silk.NET.Vulkan;
using SIMDAPI.DataAccess;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace SIMDAPI.Vulkan
{
	public class VulkanRegister
	{
		// Properties
		private VulkanService Service;
		private Instance INST;
		private Device DEV;
		private PhysicalDevice PHYS;


		// Lambda
		private string Repopath => this.Service.Repopath;
		private Vk Vk => this.Service.Vk;


		// Queue attribute
		public Queue QUE { get; private set; }


		// Memory pool (thread-safe) attribute + accessor
		private ConcurrentDictionary<int, VkMem> MemoryPool { get; } = new();
		public IReadOnlyDictionary<int, VkMem> Memory => this.MemoryPool.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);



		// Constructor
		public unsafe VulkanRegister(VulkanService vulkanService, Instance inst, Device dev, PhysicalDevice phys)
		{
			this.Service = vulkanService;
			this.INST = inst;
			this.DEV = dev;
			this.PHYS = phys;

			// Init. queue and other resources
			this.InitQueue();
		}


		// Destructor
		~VulkanRegister()
		{
			// Finalizer to ensure resources are cleaned up if Dispose is not called
			this.DisposeAsync();
		}


		// IDisposable implementation
		public async void DisposeAsync()
		{
			// Free every VkMem obj
			Parallel.ForEach(this.MemoryPool.Values, mem =>
			{
				// Free associated DeviceMemory objects
				unsafe
				{
					for (ulong i = 0; i < mem.Count; i++)
					{
						this.Vk.FreeMemory(this.DEV, mem.Memories[i], null);
					}
				}
				mem.Dispose();
			});

			// Release queue
			this.QUE = default;

			// GC
			GC.SuppressFinalize(this);

			Console.WriteLine("VulkanRegister disposed successfully.");

			await Task.CompletedTask;
		}



		// Initialize queue
		private unsafe void InitQueue()
		{
			// Get queue family properties
			uint queueFamilyCount = 0;
			this.Vk.GetPhysicalDeviceQueueFamilyProperties(this.PHYS, &queueFamilyCount, null);
			QueueFamilyProperties* queueFamilies = stackalloc QueueFamilyProperties[(int) queueFamilyCount];
			this.Vk.GetPhysicalDeviceQueueFamilyProperties(this.PHYS, &queueFamilyCount, queueFamilies);

			// Find a suitable queue family (Graphics or Compute)
			uint queueFamilyIndex = uint.MaxValue;
			for (uint i = 0; i < queueFamilyCount; i++)
			{
				if ((queueFamilies[i].QueueFlags & (QueueFlags.GraphicsBit | QueueFlags.ComputeBit)) != 0)
				{
					queueFamilyIndex = i;
					break;
				}
			}
			if (queueFamilyIndex == uint.MaxValue)
			{
				throw new Exception("No suitable queue family found.");
			}

			// Get the queue from the logical device
			Queue queue;
			this.Vk.GetDeviceQueue(this.DEV, queueFamilyIndex, 0, &queue);
			this.QUE = queue;
		}



		// Memory management
		public async Task<VkMem?> GetMemory(int hashCode)
		{
			return await Task.Run(() =>
			{
				if (this.MemoryPool.TryGetValue(hashCode, out VkMem? mem))
				{
					Console.WriteLine($"Memory found with hash code {hashCode}.");
					return mem;
				}
				else
				{
					Console.WriteLine($"No memory found with hash code {hashCode}.");
					return null;
				}
			});
		}

		public async Task<VkMem?> GetMemory(ulong handle)
		{
			return await Task.Run(() =>
			{
				var mem = this.MemoryPool.Values.FirstOrDefault(m => m.IndexHandle == handle);
				if (mem != null)
				{
					Console.WriteLine($"Memory found with handle {handle}.");
					return mem;
				}
				else
				{
					Console.WriteLine($"No memory found with handle {handle}.");
					return null;
				}
			});
		}

		public async Task<ulong> FreeMemory(int hashCode, bool readable = false)
		{
			return await Task.Run(() =>
			{
				ulong freedSize = 0;
				if (this.MemoryPool.TryRemove(hashCode, out VkMem? mem))
				{
					// Free associated DeviceMemory objects
					unsafe
					{
						for (ulong i = 0; i < mem.Count; i++)
						{
							this.Vk.FreeMemory(this.DEV, mem.Memories[i], null);
						}
					}
					Console.WriteLine($"Memory with hash code {hashCode} freed successfully.");
					freedSize = mem.TotalSize;
				}
				else
				{
					Console.WriteLine($"No memory found with hash code {hashCode} to free.");
				}

				if (readable)
				{
					freedSize /= 1024;
					freedSize /= 1024;
				}

				Console.WriteLine($"Freed memory size: {freedSize} {(readable ? "MB" : "bytes")}");

				return freedSize;
			});
		}


		// Allocating and pushing data (single)
		public async Task<VkMem?> PushDataAsync<T>(T[] data) where T : unmanaged
		{
			if (data == null || data.Length == 0)
			{
				Console.WriteLine("No data provided to push.");
				return null;
			}

			return await Task.Run(() =>
			{
				ulong size = (ulong) (data.Length * Marshal.SizeOf<T>());
				ulong offset = 0;
				VkMem? mem = null;
				DeviceMemory memory = default;
				bool memoryAllocated = false;

				try
				{
					unsafe
					{
						// Allocate memory for the data
						MemoryAllocateInfo allocInfo = new MemoryAllocateInfo
						{
							SType = StructureType.MemoryAllocateInfo,
							AllocationSize = size,
							MemoryTypeIndex = 0
						};
						if (this.Vk.AllocateMemory(this.DEV, &allocInfo, null, &memory) != Result.Success)
						{
							Console.WriteLine("Failed to allocate memory for VkMem.");
							return null;
						}
						memoryAllocated = true;

						// Copy data to the allocated memory
						void* mappedPtr = null;
						if (this.Vk.MapMemory(this.DEV, memory, offset, size, 0, &mappedPtr) != Result.Success)
						{
							Console.WriteLine("Failed to map memory.");
							return null;
						}
						System.Buffer.MemoryCopy(Unsafe.AsPointer(ref data[0]), mappedPtr, size, size);
						this.Vk.UnmapMemory(this.DEV, memory);

						// Build VkMem obj, get code
						mem = new VkMem(size, offset, memory);
						int code = mem.HashCode;

						// Tryadd the memory to the pool
						if (this.MemoryPool.TryAdd(code, mem))
						{
							Console.WriteLine($"Data pushed successfully. Size: {size}, Offset: {offset}");
							return mem;
						}
						else
						{
							// Regenerate hash code and try again
							mem.RegenerateHashCode();
							if (this.MemoryPool.TryAdd(mem.HashCode, mem))
							{
								Console.WriteLine($"Data pushed successfully after regenerating hash code. Size: {size}, Offset: {offset}");
								return mem;
							}

							Console.WriteLine("Failed to add VkMem to the memory pool.");
							mem.Dispose();
							return null;
						}
					}
				}
				catch (Exception ex)
				{
					Console.WriteLine($"Error while pushing data: {ex.Message}");
					return null;
				}
				finally
				{
					// Speicher nur freigeben, wenn er allokiert wurde und nicht im Pool ist
					if (memoryAllocated && mem != null && !this.MemoryPool.ContainsKey(mem.HashCode))
					{
						unsafe { this.Vk.FreeMemory(this.DEV, memory, null); }
					}
				}
			});
		}

		public async Task<T[]> PullDataAsync<T>(int hashCode) where T : unmanaged
		{
			return await Task.Run(async () =>
			{
				VkMem? mem = await this.GetMemory(hashCode);
				if (mem == null)
				{
					Console.WriteLine($"No memory found with hash code {hashCode}.");
					return [];
				}

				try
				{
					ulong size = mem.IndexSize;
					T[] data = new T[size / (ulong) Marshal.SizeOf<T>()];
					unsafe
					{
						void* mappedPtr = null;
						if (this.Vk.MapMemory(this.DEV, mem.Memories.FirstOrDefault(), mem.Offsets.FirstOrDefault(), size, 0, &mappedPtr) != Result.Success)
						{
							Console.WriteLine("Failed to map memory for pulling data.");
							return Array.Empty<T>();
						}
						System.Buffer.MemoryCopy(mappedPtr, Unsafe.AsPointer(ref data[0]), size, size);
						this.Vk.UnmapMemory(this.DEV, mem.Memories.FirstOrDefault());
					}
					Console.WriteLine($"Data pulled successfully. Size: {size}");
					return data;
				}
				catch (Exception ex)
				{
					Console.WriteLine($"Error while pulling data: {ex.Message}");
					return Array.Empty<T>();
				}
			});
		}



		// Allocating and pushing data (multi)
		public async Task<VkMem?> PushChunksAsync<T>(IEnumerable<T[]> chunks) where T : unmanaged
		{
			var chunkList = chunks.ToList();
			int n = chunkList.Count;
			var tasks = new Task<VkMem?>[n];

			// Parallel push, Reihenfolge bleibt erhalten
			for (int i = 0; i < n; i++)
			{
				int idx = i;
				tasks[idx] = PushDataAsync(chunkList[idx]);
			}

			var results = await Task.WhenAll(tasks);

			// Filtere nulls, aber behalte Reihenfolge
			var valid = results
				.Select((mem, i) => (mem, i))
				.Where(x => x.mem != null)
				.ToList();

			if (valid.Count == 0)
				return null;

			// Arrays für Multi-Buffer-VkMem bauen
			ulong[] sizes = valid.Select(x => x.mem!.Sizes[0]).ToArray();
			ulong[] offsets = valid.Select(x => x.mem!.Offsets[0]).ToArray();
			DeviceMemory[] memories = valid.Select(x => x.mem!.Memories[0]).ToArray();

			return new VkMem(sizes, offsets, memories);
		}

		public async Task<IEnumerable<T[]>> PullChunksAsync<T>(int hashCode) where T : unmanaged
		{
			// Hole das Multi-Buffer-VkMem
			VkMem? mem = await GetMemory(hashCode);
			if (mem == null)
			{
				Console.WriteLine($"No memory found with hash code {hashCode}.");
				return Enumerable.Empty<T[]>();
			}

			int n = mem.Sizes.Length;
			var tasks = new Task<T[]>[n];

			// Parallel pull für jeden Buffer
			for (int i = 0; i < n; i++)
			{
				int idx = i;
				tasks[idx] = Task.Run(() =>
				{
					ulong size = mem.Sizes[idx];
					ulong offset = mem.Offsets[idx];
					DeviceMemory memory = mem.Memories[idx];
					T[] data = new T[size / (ulong) Marshal.SizeOf<T>()];
					unsafe
					{
						void* mappedPtr = null;
						if (this.Vk.MapMemory(this.DEV, memory, offset, size, 0, &mappedPtr) != Result.Success)
						{
							Console.WriteLine($"Failed to map memory for chunk {idx}.");
							return Array.Empty<T>();
						}
						System.Buffer.MemoryCopy(mappedPtr, Unsafe.AsPointer(ref data[0]), size, size);
						this.Vk.UnmapMemory(this.DEV, memory);
					}
					return data;
				});
			}

			var results = await Task.WhenAll(tasks);
			return results;
		}



		// Accessor for ImageCollection (ImgObj)
		public async Task<IntPtr> MoveImageAsync(ImgObj obj)
		{
			// Push if OnHost
			if (obj.OnHost)
			{
				// Get bytes & null img
				byte[] data = obj.GetBytes();

				// Push -> VkMem
				VkMem? mem = await this.PushDataAsync(data);
				if (mem == null)
				{
					Console.WriteLine("Failed to push image data to Vulkan memory.");
					return IntPtr.Zero;
				}

				obj.Pointer = (IntPtr) mem.IndexHandle;

				Console.WriteLine($"Image data pushed successfully. Memory handle: {obj.Pointer}");
				return obj.Pointer;
			}

			// Else pull if OnDevice
			else if (obj.OnDevice)
			{
				// Get ulong handle from Pointer -> GetMemory
				ulong handle = (ulong) obj.Pointer;
				VkMem? mem = await this.GetMemory(handle);
				if (mem == null)
				{
					Console.WriteLine($"No memory found with handle {handle}.");
					return IntPtr.Zero;
				}

				// Pull data from VkMem
				byte[] data = await this.PullDataAsync<byte>(mem.HashCode);

				// Set img in obj & null Pointer
				obj.SetImage(data);

				Console.WriteLine($"Image data pulled successfully. Memory handle: {obj.Pointer}");

				return obj.Pointer;
			}

			// Otherwise, return IntPtr.Zero
			else
			{
				Console.WriteLine("Image is neither on host nor on device.");
				return IntPtr.Zero;
			}
		}


		// Accessor for AudioCollection (AudioObj)
		// 

	}


	public class VkMem : IDisposable
	{
		// HashCode (Identifier)
		public int HashCode { get; private set; } = 0;


		// Properties (Memory, Sizes, Offsets, Type)
		public ulong[] Sizes { get; private set; }
		public ulong[] Offsets { get; private set; }
		public DeviceMemory[] Memories { get; private set; }
		public DeviceMemory Memory => this.Memories.FirstOrDefault();
		public Type MemoryType { get; private set; } = Type.EmptyTypes[0];

		// Lambda (accessible properties)
		public ulong Count => (ulong) this.Sizes.LongLength;
		public ulong IndexSize => (ulong) this.Sizes.FirstOrDefault();
		public ulong IndexOffset => (ulong) this.Offsets.FirstOrDefault();
		public ulong TotalSize => (ulong) this.Sizes.Sum(s => (long) s);
		public ulong TotalOffset => (ulong) this.Offsets.Sum(o => (long) o);
		public ulong IndexHandle => this.Memories.FirstOrDefault().Handle;



		// Single-buffer constructor
		public VkMem(ulong size, ulong offset, DeviceMemory memory)
		{
			this.Sizes = [size];
			this.Offsets = [offset];
			this.Memories = [memory];

			this.MemoryType = memory.GetType();

			this.RegenerateHashCode();
		}

		// Multi-buffer constructor
		public VkMem(ulong[] sizes, ulong[] offsets, DeviceMemory[] memories)
		{
			if (sizes.Length != offsets.Length || sizes.Length != memories.Length)
			{
				throw new ArgumentException("Sizes, offsets, and memories must have the same length.");
			}

			this.Sizes = sizes;
			this.Offsets = offsets;
			this.Memories = memories;

			this.MemoryType = memories.FirstOrDefault().GetType();

			this.RegenerateHashCode();
		}

		// Deconstructor
		~VkMem()
		{
			// Finalizer to ensure memory is freed if Dispose is not called
			this.Dispose();
		}



		// IDisposable implementation
		public void Dispose()
		{
			// Free the memory associated with this VkMem object
			GC.SuppressFinalize(this);
		}



		// HashCode method
		public void RegenerateHashCode(int seed = 0)
		{
			if (seed == 0)
			{
				seed = Environment.TickCount % int.MaxValue;
			}

			this.HashCode = this.MemoryType.GetHashCode() ^ this.Sizes.GetHashCode() ^ this.Offsets.GetHashCode() ^ this.Memories.FirstOrDefault().Handle.GetHashCode() ^ seed;
		}

	}
}
