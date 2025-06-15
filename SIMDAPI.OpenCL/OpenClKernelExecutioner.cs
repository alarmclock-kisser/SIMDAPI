
using OpenTK.Compute.OpenCL;
using OpenTK.Mathematics;
using SIMDAPI.DataAccess;
using System.Diagnostics;

namespace SIMDAPI.OpenCL
{
	public class OpenClKernelExecutioner
	{
		// ----- ----- -----  ATTRIBUTES  ----- ----- ----- \\
		private string Repopath;
		private OpenClMemoryRegister MemR;
		private CLContext Context;
		private CLDevice Device;
		private CLPlatform Platform;
		private CLCommandQueue Queue;
		private OpenClKernelCompiler Compiler;






		// ----- ----- -----  LAMBDA  ----- ----- ----- \\
		public CLKernel? Kernel => this.Compiler?.Kernel;
		public string? KernelFile => this.Compiler?.KernelFile;




		// ----- ----- -----  CONSTRUCTOR ----- ----- ----- \\
		public OpenClKernelExecutioner(string repopath, OpenClMemoryRegister memR, CLContext context, CLDevice device, CLPlatform platform, CLCommandQueue queue, OpenClKernelCompiler compiler)
		{
			this.Repopath = repopath;
			this.MemR = memR;
			this.Context = context;
			this.Device = device;
			this.Platform = platform;
			this.Queue = queue;
			this.Compiler = compiler;
		}






		// ----- ----- -----  METHODS  ----- ----- ----- \\
		public void Log(string message = "", string inner = "", int indent = 0)
		{
			string msg = "[Exec]: " + new string(' ', indent * 2) + message;

			if (!string.IsNullOrEmpty(inner))
			{
				msg += " (" + inner + ")";
			}

			// Invoke optionally
			Console.WriteLine(msg);
		}


		public void Dispose()
		{
			// Dispose logic here
			
		}





		// EXEC
		public IntPtr ExecuteKernelGenericImage(string baseName = "NULL", string version = "01", IntPtr pointer = 0, int width = 0, int height = 0, int channels = 4, int bitdepth = 8, object[]? variableArguments = null, bool logSuccess = false)
		{
			// Start stopwatch
			List<long> times = [];
			List<string> timeNames = ["load: ", "mem: ", "args: ", "exec: ", "total: "];
			Stopwatch sw = Stopwatch.StartNew();

			// Get kernel path
			string kernelPath = this.Compiler.Files.FirstOrDefault(f => f.Key.Contains(baseName + version)).Key ?? "";

			// Load kernel if not loaded
			if (this.Kernel == null || this.KernelFile != kernelPath)
			{
				this.Compiler.LoadKernel(baseName + version);
				if (this.Kernel == null || this.KernelFile == null || !this.KernelFile.Contains("\\Imaging\\"))
				{
					this.Log("Could not load Kernel '" + baseName + version + "'", $"ExecuteKernelIPGeneric({string.Join(", ", variableArguments ?? [])})");
					return pointer;
				}
			}

			// Take time
			times.Add(sw.ElapsedMilliseconds - times.Sum());

			// Get input buffer & length
			ClMem? inputMem = this.MemR.GetBuffer(pointer);
			if (inputMem == null)
			{
				this.Log("Input buffer not found or invalid length: " + pointer.ToString("X16"), "", 2);
				return pointer;
			}

			// Get kernel arguments & work dimensions
			List<string> argNames = this.Compiler.Arguments.Keys.ToList();

			// Dimensions
			int pixelsTotal = (int) inputMem.IndexLength / 4; // Anzahl der Pixel
			int workWidth = width > 0 ? width : pixelsTotal; // Falls kein width gegeben, 1D
			int workHeight = height > 0 ? height : 1;        // Falls kein height, 1D

			// Work dimensions
			uint workDim = (width > 0 && height > 0) ? 2u : 1u;
			UIntPtr[] globalWorkSize = workDim == 2
				? [(UIntPtr) workWidth, (UIntPtr) workHeight]
				: [(UIntPtr) pixelsTotal];

			// Create output buffer
			IntPtr outputPointer = IntPtr.Zero;
			if (this.Compiler.GetArgumentPointerCount() == 0)
			{
				if (logSuccess)
				{
					this.Log("No output buffer needed", "No output buffer", 1);
				}
				return pointer;
			}
			else if (this.Compiler.GetArgumentPointerCount() == 1)
			{
				if (logSuccess)
				{
					this.Log("Single pointer kernel detected", "Single pointer kernel", 1);
				}
			}
			else if (this.Compiler.GetArgumentPointerCount() >= 2)
			{
				ClMem? outputMem = this.MemR.AllocateSingle<byte>(inputMem.IndexLength);
				if (outputMem == null)
				{
					if (logSuccess)
					{
						this.Log("Error allocating output buffer", "", 2);
					}
					return pointer;
				}
				outputPointer = outputMem.IndexHandle;
			}

			// Take time
			times.Add(sw.ElapsedMilliseconds - times.Sum());

			// Merge arguments
			List<object> arguments = this.MergeArgumentsImage(variableArguments ?? [], pointer, outputPointer, width, height, channels, bitdepth, false);

			// Set kernel arguments
			for (int i = 0; i < arguments.Count; i++)
			{
				// Set argument
				CLResultCode err = this.SetKernelArgSafe((uint) i, arguments[i]);
				if (err != CLResultCode.Success)
				{
					this.Log("Error setting kernel argument " + i + ": " + err.ToString(), arguments[i].ToString() ?? "");
					return pointer;
				}
			}

			// Take time
			times.Add(sw.ElapsedMilliseconds - times.Sum());

			// Log arguments
			/*if (logSuccess)
			{
				this.Log("Kernel arguments set: " + string.Join(", ", argNames.Select((a, i) => a + ": " + arguments[Math.Min(arguments.Count, i)].ToString())), "'" + baseName + version + "'", 2);
			}*/

			// Exec
			CLResultCode error = CL.EnqueueNDRangeKernel(
				this.Queue,
				this.Kernel.Value,
				workDim,          // 1D oder 2D
				null,             // Kein Offset
				globalWorkSize,   // Work-Größe in Pixeln
				null,             // Lokale Work-Size (automatisch)
				0, null, out CLEvent evt
			);
			if (error != CLResultCode.Success)
			{
				this.Log("Error executing kernel: " + error.ToString(), "", 2);
				return pointer;
			}

			// Wait for kernel to finish
			error = CL.WaitForEvents(1, [evt]);
			if (error != CLResultCode.Success)
			{
				this.Log("Error waiting for kernel to finish: " + error.ToString(), "", 2);
				return pointer;
			}

			// Release event
			error = CL.ReleaseEvent(evt);
			if (error != CLResultCode.Success)
			{
				this.Log("Error releasing event: " + error.ToString(), "", 2);
				return pointer;
			}

			// Take time
			times.Add(sw.ElapsedMilliseconds - times.Sum());
			times.Add(times.Sum());
			sw.Stop();

			// Free input buffer
			long freed;
			if (outputPointer == IntPtr.Zero)
			{
				freed = 0;
			}
			else
			{
				freed = this.MemR.FreeBuffer(pointer, true);
			}

			// Log success with timeNames
			if (logSuccess)
			{
				this.Log("Kernel executed successfully! Times: " + string.Join(", ", times.Select((t, i) => timeNames[i] + t + "ms")) + "(freed input: " + freed + "MB)", "'" + baseName + version + "'", 1);
			}

			// Return valued pointer
			return outputPointer != IntPtr.Zero ? outputPointer : pointer;
		}




		// Helpers
		public List<object> MergeArgumentsImage(object[] arguments, IntPtr inputPointer = 0, IntPtr outputPointer = 0, int width = 0, int height = 0, int channels = 4, int bitdepth = 8, bool log = false)
		{
			List<object> result = [];

			// Get kernel arguments
			Dictionary<string, Type> kernelArguments = this.Compiler.GetKernelArguments(this.Kernel);
			if (kernelArguments.Count == 0)
			{
				this.Log("Kernel arguments not found", "", 2);
				kernelArguments = this.Compiler.GetKernelArgumentsAnalog(this.KernelFile);
				if (kernelArguments.Count == 0)
				{
					this.Log("Kernel arguments not found", "", 2);
					return [];
				}
			}
			int bpp = bitdepth * channels;

			// Match arguments to kernel arguments
			bool inputFound = false;
			for (int i = 0; i < kernelArguments.Count; i++)
			{
				string argName = kernelArguments.ElementAt(i).Key;
				Type argType = kernelArguments.ElementAt(i).Value;

				// If argument is pointer -> add pointer
				if (argType.Name.EndsWith("*"))
				{
					// Get pointer value
					IntPtr argPointer = 0;
					if (!inputFound)
					{
						argPointer = arguments[i] is IntPtr ? (IntPtr) arguments[i] : inputPointer;
						inputFound = true;
					}
					else
					{
						argPointer = arguments[i] is IntPtr ? (IntPtr) arguments[i] : outputPointer;
					}

					// Get buffer
					ClMem? argBuffer = this.MemR.GetBuffer(argPointer);
					if (argBuffer == null || argBuffer.IndexLength == IntPtr.Zero)
					{
						this.Log("Argument buffer not found or invalid length: " + argPointer.ToString("X16"), argBuffer?.IndexLength.ToString() ?? "None", 2);
						return [];
					}
					CLBuffer buffer = argBuffer.Buffers.FirstOrDefault();

					// Add pointer to result
					result.Add(buffer);

					// Log buffer found
					if (log)
					{
						// Log buffer found
						this.Log("Kernel argument buffer found: " + argPointer.ToString("X16"), "Index: " + i, 3);
					}
				}
				else if (argType == typeof(int))
				{
					// If name is "width" or "height" -> add width or height
					if (argName.ToLower() == "width")
					{
						result.Add(width <= 0 ? arguments[i] : width);

						// Log width found
						if (log)
						{
							this.Log("Kernel argument width found: " + width.ToString(), "Index: " + i, 3);
						}
					}
					else if (argName.ToLower() == "height")
					{
						result.Add(height <= 0 ? arguments[i] : height);

						// Log height found
						if (log)
						{
							this.Log("Kernel argument height found: " + height.ToString(), "Index: " + i, 3);
						}
					}
					else if (argName.ToLower() == "channels")
					{
						result.Add(channels <= 0 ? arguments[i] : channels);

						// Log channels found
						if (log)
						{
							this.Log("Kernel argument channels found: " + channels.ToString(), "Index: " + i, 3);
						}
					}
					else if (argName.ToLower() == "bitdepth")
					{
						result.Add(bitdepth <= 0 ? arguments[i] : bitdepth);

						// Log channels found
						if (log)
						{
							this.Log("Kernel argument bitdepth found: " + bitdepth.ToString(), "Index: " + i, 3);
						}
					}
					else if (argName.ToLower() == "bpp")
					{
						result.Add(bpp <= 0 ? arguments[i] : bpp);

						// Log channels found
						if (log)
						{
							this.Log("Kernel argument bpp found: " + bpp.ToString(), "Index: " + i, 3);
						}
					}
					else
					{
						result.Add((int) arguments[Math.Min(arguments.Length - 1, i)]);
					}
				}
				else if (argType == typeof(float))
				{
					// Sicher konvertieren
					result.Add(Convert.ToSingle(arguments[i]));
				}
				else if (argType == typeof(double))
				{
					result.Add(Convert.ToDouble(arguments[i]));
				}
				else if (argType == typeof(long))
				{
					result.Add((long) arguments[i]);
				}
			}

			// Log arguments
			if (log)
			{
				this.Log("Kernel arguments: " + string.Join(", ", result.Select(a => a.ToString())), "'" + Path.GetFileName(this.KernelFile) + "'", 2);
			}

			return result;
		}

		public CLResultCode SetKernelArgSafe(uint index, object value)
		{
			// Check kernel
			if (this.Kernel == null)
			{
				this.Log("Kernel is null");
				return CLResultCode.InvalidKernelDefinition;
			}

			switch (value)
			{
				case CLBuffer buffer:
					return CL.SetKernelArg(this.Kernel.Value, index, buffer);

				case int i:
					return CL.SetKernelArg(this.Kernel.Value, index, i);

				case long l:
					return CL.SetKernelArg(this.Kernel.Value, index, l);

				case float f:
					return CL.SetKernelArg(this.Kernel.Value, index, f);

				case double d:
					return CL.SetKernelArg(this.Kernel.Value, index, d);

				case byte b:
					return CL.SetKernelArg(this.Kernel.Value, index, b);

				case IntPtr ptr:
					return CL.SetKernelArg(this.Kernel.Value, index, ptr);

				// Spezialfall für lokalen Speicher (Größe als uint)
				case uint u:
					return CL.SetKernelArg(this.Kernel.Value, index, new IntPtr(u));

				// Fall für Vector2
				case Vector2 v:
					// Vector2 ist ein Struct, daher muss es als Array übergeben werden
					return CL.SetKernelArg(this.Kernel.Value, index, v);

				default:
					throw new ArgumentException($"Unsupported argument type: {value?.GetType().Name ?? "null"}");
			}
		}

		private uint GetMaxWorkGroupSize()
		{
			const uint FALLBACK_SIZE = 64;
			const string FUNCTION_NAME = "GetMaxWorkGroupSize";

			if (!this.Kernel.HasValue)
			{
				this.Log("Kernel not initialized", FUNCTION_NAME, 2);
				return FALLBACK_SIZE;
			}

			try
			{
				// 1. Zuerst die benötigte Puffergröße ermitteln
				CLResultCode result = CL.GetKernelWorkGroupInfo(
					this.Kernel.Value,
					this.Device,
					KernelWorkGroupInfo.WorkGroupSize,
					UIntPtr.Zero,
					null,
					out nuint requiredSize);

				if (result != CLResultCode.Success || requiredSize == 0)
				{
					this.Log($"Failed to get required size: {result}", FUNCTION_NAME, 2);
					return FALLBACK_SIZE;
				}

				// 2. Puffer mit korrekter Größe erstellen
				byte[] paramValue = new byte[requiredSize];

				// 3. Tatsächliche Abfrage durchführen
				result = CL.GetKernelWorkGroupInfo(
					this.Kernel.Value,
					this.Device,
					KernelWorkGroupInfo.WorkGroupSize,
					new UIntPtr(requiredSize),
					paramValue,
					out _);

				if (result != CLResultCode.Success)
				{
					this.Log($"Failed to get work group size: {result}", FUNCTION_NAME, 2);
					return FALLBACK_SIZE;
				}

				// 4. Ergebnis konvertieren (abhängig von der Plattform)
				uint maxSize;
				if (requiredSize == sizeof(uint))
				{
					maxSize = BitConverter.ToUInt32(paramValue, 0);
				}
				else if (requiredSize == sizeof(ulong))
				{
					maxSize = (uint) BitConverter.ToUInt64(paramValue, 0);
				}
				else
				{
					this.Log($"Unexpected return size: {requiredSize}", FUNCTION_NAME, 2);
					return FALLBACK_SIZE;
				}

				// 5. Gültigen Wert sicherstellen
				if (maxSize == 0)
				{
					this.Log("Device reported max work group size of 0", FUNCTION_NAME, 2);
					return FALLBACK_SIZE;
				}

				return maxSize;
			}
			catch (Exception ex)
			{
				this.Log($"Error in {FUNCTION_NAME}: {ex.Message}", ex.StackTrace ?? "", 3);
				return FALLBACK_SIZE;
			}
		}



		// ----- ----- ----- ACCESSIBLE METHODS ----- ----- ----- \\
		public IntPtr ExecKernelImage(ImgObj obj, string kernelName = "", string kernelVersion = "00", object[]? variableArguments = null, bool log = false)
		{
			// Verify obj on device
			bool moved = false;
			if (obj.OnHost)
			{
				if (log)
				{
					this.Log("Image was on host, pushing ...", obj.Width + " x " + obj.Height, 2);
				}

				// Get pixel bytes
				byte[] pixels = obj.GetBytes();
				if (pixels == null || pixels.LongLength == 0)
				{
					this.Log("Couldn't get byte[] from image object", "Aborting", 1);
					return IntPtr.Zero;
				}

				// Push pixels -> pointer
				obj.Pointer = this.MemR.PushData<byte>(pixels)?.IndexHandle ?? IntPtr.Zero;
				if (obj.OnHost || obj.Pointer == IntPtr.Zero)
				{
					if (log)
					{
						this.Log("Couldn't get pointer after pushing pixels to device", pixels.LongLength.ToString("N0"), 1);
					}
					return IntPtr.Zero;
				}

				moved = true;
			}

			// Get parameters for call
			IntPtr pointer = obj.Pointer;
			int width = obj.Width;
			int height = obj.Height;
			int channels = obj.Channels;
			int bitdepth = obj.Bitdepth;

			// Call exec on image
			IntPtr outputPointer = this.ExecuteKernelGenericImage(kernelName, kernelVersion, pointer, width, height, channels, bitdepth, variableArguments, log);
			if (outputPointer == IntPtr.Zero)
			{
				if (log)
				{
					this.Log("Couldn't get output pointer after kernel execution", "Aborting", 1);
				}
				return outputPointer;
			}

			// Set obj pointer
			obj.Pointer = outputPointer;

			// Optionally: Move back to host
			if (obj.OnDevice && moved)
			{
				// Pull pixel bytes
				byte[] pixels = this.MemR.PullData<byte>(obj.Pointer);
				if (pixels == null || pixels.LongLength == 0)
				{
					if (log)
					{
						this.Log("Couldn't pull pixels (byte[]) from device", "Aborting", 1);
					}
					return IntPtr.Zero;
				}

				// Aggregate image
				obj.SetImage(pixels);
			}

			return outputPointer;
		}


		public async Task<IntPtr> ExecKernelImageAsync(ImgObj obj, string kernelName = "", string kernelVersion = "00", object[]? variableArguments = null, bool log = false)
		{
			return await Task.Run(() => this.ExecKernelImage(obj, kernelName, kernelVersion, variableArguments, log));
		}
	}
}