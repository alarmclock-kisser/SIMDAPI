using OpenTK.Audio.OpenAL;
using OpenTK.Compute.OpenCL;
using OpenTK.Mathematics;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace SIMDAPI.OpenCL
{
	public class OpenClKernelCompiler
	{
		private string Repopath;
		private OpenClMemoryRegister MemR;
		private CLContext Context;
		private CLDevice Device;
		private CLPlatform Platform;
		private CLCommandQueue Queue;



		// ----- ----- ----- ATTRIBUTES ----- ----- ----- \\
		public CLKernel? Kernel = null;
		public string? KernelFile = null;

		public long InputBufferPointer = 0;


		public Dictionary<CLKernel, string> KernelCache = [];



		// ----- ----- ----- LAMBDA ----- ----- ----- \\
		public Dictionary<string, string> Files => this.GetKernelFiles();

		public Dictionary<string, Type> Arguments => this.GetKernelArguments();




		// ----- ----- ----- CONSTRUCTORS ----- ----- ----- \\
		public OpenClKernelCompiler(string repopath, OpenClMemoryRegister memorRegister, CLContext ctx, CLDevice dev, CLPlatform plat, CLCommandQueue que)
		{
			// Set attributes
			this.Repopath = repopath;
			this.MemR = memorRegister;
			this.Context = ctx;
			this.Device = dev;
			this.Platform = plat;
			this.Queue = que;

			//this.PrecompileAllKernels(true);

		}




		// ----- ----- ----- METHODS ----- ----- ----- \\





		// ----- ----- ----- PUBLIC METHODS ----- ----- ----- \\
		// Log
		public void Log(string message = "", string inner = "", int indent = 0)
		{
			string msg = "[Kernel]: " + new string(' ', indent * 2) + message;

			if (!string.IsNullOrEmpty(inner))
			{
				msg += " (" + inner + ")";
			}

			// Invoke optionally
			Console.WriteLine(msg);
		}



		// Dispose
		public void Dispose()
		{
			// Dispose logic here
			this.Kernel = null;
			this.KernelFile = null;
		}


		// Files
		public Dictionary<string, string> GetKernelFiles(string subdir = "Kernels")
		{
			string dir = Path.Combine(this.Repopath, subdir);

			// Build dir if it doesn't exist
			if (!Directory.Exists(dir))
			{
				Directory.CreateDirectory(dir);
			}

			// Get all .cl files in the directory
			string[] files = Directory.GetFiles(dir, "*.cl", SearchOption.AllDirectories);

			// Check if any files were found
			if (files.Length == 0)
			{
				this.Log("No kernel files found in directory: " + dir);
				return [];
			}

			// Verify each file
			Dictionary<string, string> verifiedFiles = [];
			foreach (string file in files)
			{
				string? verifiedFile = this.VerifyKernelFile(file);
				if (verifiedFile != null)
				{
					string? name = this.GetKernelName(verifiedFile);
					verifiedFiles.Add(verifiedFile, name ?? "N/A");
				}
			}

			// Return
			return verifiedFiles;
		}

		public string? VerifyKernelFile(string filePath)
		{
			// Check if file exists & is .cl
			if (!File.Exists(filePath))
			{
				this.Log("Kernel file not found: " + filePath);
				return null;
			}

			if (Path.GetExtension(filePath) != ".cl")
			{
				this.Log("Kernel file is not a .cl file: " + filePath);
				return null;
			}

			// Check if file is empty
			string[] lines = File.ReadAllLines(filePath);
			if (lines.Length == 0)
			{
				this.Log("Kernel file is empty: " + filePath);
				return null;
			}

			// Check if file contains kernel function
			if (!lines.Any(line => line.Contains("__kernel")))
			{
				this.Log("Kernel function not found in file: " + filePath);
				return null;
			}

			return Path.GetFullPath(filePath);
		}

		public string? GetKernelName(string filePath)
		{
			// Verify file
			string? verifiedFilePath = this.VerifyKernelFile(filePath);
			if (verifiedFilePath == null)
			{
				return null;
			}

			// Try to extract function name from kernel code text
			string code = File.ReadAllText(filePath);

			// Find index of first "__kernel void "
			int index = code.IndexOf("__kernel void ");
			if (index == -1)
			{
				this.Log("Kernel function not found in file: " + filePath);
				return null;
			}

			// Find index of first "(" after "__kernel void "
			int startIndex = index + "__kernel void ".Length;
			int endIndex = code.IndexOf("(", startIndex);
			if (endIndex == -1)
			{
				this.Log("Kernel function not found in file: " + filePath);
				return null;
			}

			// Extract function name
			string functionName = code.Substring(startIndex, endIndex - startIndex).Trim();
			if (functionName.Contains(" ") || functionName.Contains("\t") ||
				functionName.Contains("\n") || functionName.Contains("\r"))
			{
				this.Log("Kernel function name is invalid: " + functionName);
			}

			// Check if function name is empty
			if (string.IsNullOrEmpty(functionName))
			{
				this.Log("Kernel function name is empty: " + filePath);
				return null;
			}

			// Compare to file name without ext
			string fileName = Path.GetFileNameWithoutExtension(filePath);
			if (string.Compare(functionName, fileName, StringComparison.OrdinalIgnoreCase) != 0)
			{
				this.Log("Kernel function name does not match file name: " + filePath, "", 2);
			}

			return functionName;
		}


		// Compile
		public CLKernel? CompileFile(string filePath)
		{
			// Verify file
			string? verifiedFilePath = this.VerifyKernelFile(filePath);
			if (verifiedFilePath == null)
			{
				return null;
			}

			// Get kernel name
			string? kernelName = this.GetKernelName(verifiedFilePath);
			if (kernelName == null)
			{
				return null;
			}

			// Read kernel code
			string code = File.ReadAllText(verifiedFilePath);

			// Create program
			CLProgram program = CL.CreateProgramWithSource(this.Context, code, out CLResultCode error);
			if (error != CLResultCode.Success)
			{
				this.Log("Error creating program from source: " + error.ToString());
				return null;
			}

			// Create callback
			CL.ClEventCallback callback = new((program, userData) =>
			{
				// Check build log
				//
			});

			// When building the kernel
			string buildOptions = "-cl-std=CL1.2 -cl-fast-relaxed-math";
			CL.BuildProgram(program, 1, [this.Device], buildOptions, 0, IntPtr.Zero);

			// Build program
			error = CL.BuildProgram(program, [this.Device], buildOptions, callback);
			if (error != CLResultCode.Success)
			{
				this.Log("Error building program: " + error.ToString());

				// Get build log
				CLResultCode error2 = CL.GetProgramBuildInfo(program, this.Device, ProgramBuildInfo.Log, out byte[] buildLog);
				if (error2 != CLResultCode.Success)
				{
					this.Log("Error getting build log: " + error2.ToString());
				}
				else
				{
					string log = Encoding.UTF8.GetString(buildLog);
					this.Log("Build log: " + log, "", 1);
				}

				CL.ReleaseProgram(program);
				return null;
			}

			// Create kernel
			CLKernel kernel = CL.CreateKernel(program, kernelName, out error);
			if (error != CLResultCode.Success)
			{
				this.Log("Error creating kernel: " + error.ToString());

				// Get build log
				CLResultCode error2 = CL.GetProgramBuildInfo(program, this.Device, ProgramBuildInfo.Log, out byte[] buildLog);
				if (error2 != CLResultCode.Success)
				{
					this.Log("Error getting build log: " + error2.ToString());
				}
				else
				{
					string log = Encoding.UTF8.GetString(buildLog);
					this.Log("Build log: " + log, "", 1);
				}

				CL.ReleaseProgram(program);
				return null;
			}

			// Return kernel
			return kernel;
		}

		public Dictionary<string, Type> GetKernelArguments(CLKernel? kernel = null, string filePath = "")
		{
			Dictionary<string, Type> arguments = [];

			// Verify kernel
			kernel ??= this.Kernel;
			if (kernel == null)
			{
				// Try get kernel by file path
				kernel = this.CompileFile(filePath);
				if (kernel == null)
				{
					this.Log("Kernel is null");
					return arguments;
				}
			}

			// Get kernel info
			CLResultCode error = CL.GetKernelInfo(kernel.Value, KernelInfo.NumberOfArguments, out byte[] argCountBytes);
			if (error != CLResultCode.Success)
			{
				//this.Log("Error getting kernel info: " + error.ToString());
				return arguments;
			}

			// Get number of arguments
			int argCount = BitConverter.ToInt32(argCountBytes, 0);

			// Loop through arguments
			for (int i = 0; i < argCount; i++)
			{
				// Get argument info type name
				error = CL.GetKernelArgInfo(kernel.Value, (uint) i, KernelArgInfo.TypeName, out byte[] argTypeBytes);
				if (error != CLResultCode.Success)
				{
					//this.Log("Error getting kernel argument info: " + error.ToString());
					continue;
				}

				// Get argument info arg name
				error = CL.GetKernelArgInfo(kernel.Value, (uint) i, KernelArgInfo.Name, out byte[] argNameBytes);
				if (error != CLResultCode.Success)
				{
					//this.Log("Error getting kernel argument info: " + error.ToString());
					continue;
				}

				// Get argument type & name
				string argName = Encoding.UTF8.GetString(argNameBytes).TrimEnd('\0');
				string typeName = Encoding.UTF8.GetString(argTypeBytes).TrimEnd('\0');
				Type? type = null;

				// Switch for typeName
				if (typeName.EndsWith("*"))
				{
					typeName = typeName.Replace("*", "").ToLower();
					switch (typeName)
					{
						case "int":
							type = typeof(int*);
							break;
						case "float":
							type = typeof(float*);
							break;
						case "long":
							type = typeof(long*);
							break;
						case "uchar":
							type = typeof(byte*);
							break;
						case "vector2":
							type = typeof(Vector2*);
							break;
						default:
							this.Log("Unknown pointer type: " + typeName, "", 2);
							break;
					}
				}
				else
				{
					switch (typeName)
					{
						case "int":
							type = typeof(int);
							break;
						case "float":
							type = typeof(float);
							break;
						case "double":
							type = typeof(double);
							break;
						case "char":
							type = typeof(char);
							break;
						case "uchar":
							type = typeof(byte);
							break;
						case "short":
							type = typeof(short);
							break;
						case "ushort":
							type = typeof(ushort);
							break;
						case "long":
							type = typeof(long);
							break;
						case "ulong":
							type = typeof(ulong);
							break;
						case "vector2":
							type = typeof(Vector2);
							break;
						default:
							this.Log("Unknown argument type: " + typeName, "", 2);
							break;
					}
				}

				// Add to dictionary
				arguments.Add(argName, type ?? typeof(object));
			}

			// Return arguments
			return arguments;
		}

		public Dictionary<string, Type> GetKernelArgumentsAnalog(string? filepath)
		{
			Dictionary<string, Type> arguments = [];
			if (string.IsNullOrEmpty(filepath))
			{
				filepath = this.KernelFile;
			}

			// Read kernel code
			filepath = this.VerifyKernelFile(filepath ?? "");
			if (filepath == null)
			{
				this.Log("Kernel file not found or invalid: " + filepath);
				return arguments;
			}

			string code = File.ReadAllText(filepath);
			if (string.IsNullOrEmpty(code))
			{
				this.Log("Kernel code is empty: " + filepath);
				return arguments;
			}

			// Find kernel function
			int index = code.IndexOf("__kernel void ");
			if (index == -1)
			{
				this.Log("Kernel function not found in file: " + filepath);
				return arguments;
			}
			int startIndex = index + "__kernel void ".Length;
			int endIndex = code.IndexOf("(", startIndex);
			if (endIndex == -1)
			{
				this.Log("Kernel function not found in file: " + filepath);
				return arguments;
			}

			string functionName = code.Substring(startIndex, endIndex - startIndex).Trim();
			if (string.IsNullOrEmpty(functionName))
			{
				this.Log("Kernel function name is empty: " + filepath);
				return arguments;
			}

			if (functionName.Contains(" ") || functionName.Contains("\t") ||
				functionName.Contains("\n") || functionName.Contains("\r"))
			{
				this.Log("Kernel function name is invalid: " + functionName, "", 2);
			}

			// Get arguments string
			int argsStartIndex = code.IndexOf("(", endIndex) + 1;
			int argsEndIndex = code.IndexOf(")", argsStartIndex);
			if (argsEndIndex == -1)
			{
				this.Log("Kernel arguments not found in file: " + filepath);
				return arguments;
			}
			string argsString = code.Substring(argsStartIndex, argsEndIndex - argsStartIndex).Trim();
			if (string.IsNullOrEmpty(argsString))
			{
				this.Log("Kernel arguments are empty: " + filepath);
				return arguments;
			}

			string[] args = argsString.Split(',');

			foreach (string arg in args)
			{
				string[] parts = arg.Trim().Split(' ');
				if (parts.Length < 2)
				{
					this.Log("Kernel argument is invalid: " + arg, "", 2);
					continue;
				}
				string typeName = parts[^2].Trim();
				string argName = parts[^1].Trim().TrimEnd(';', ')', '\n', '\r', '\t');
				Type? type = null;
				if (typeName.EndsWith("*"))
				{
					typeName = typeName.Replace("*", "");
					switch (typeName)
					{
						case "int":
							type = typeof(int*);
							break;
						case "float":
							type = typeof(float*);
							break;
						case "long":
							type = typeof(long*);
							break;
						case "uchar":
							type = typeof(byte*);
							break;
						case "Vector2":
							type = typeof(Vector2*);
							break;
						default:
							this.Log("Unknown pointer type: " + typeName, "", 2);
							break;
					}
				}
				else
				{
					switch (typeName)
					{
						case "int":
							type = typeof(int);
							break;
						case "float":
							type = typeof(float);
							break;
						case "double":
							type = typeof(double);
							break;
						case "char":
							type = typeof(char);
							break;
						case "uchar":
							type = typeof(byte);
							break;
						case "short":
							type = typeof(short);
							break;
						case "ushort":
							type = typeof(ushort);
							break;
						case "long":
							type = typeof(long);
							break;
						case "ulong":
							type = typeof(ulong);
							break;
						case "Vector2":
							type = typeof(Vector2);
							break;
						default:
							this.Log("Unknown argument type: " + typeName, "", 2);
							break;
					}
				}
				if (type != null)
				{
					arguments.Add(argName, type ?? typeof(object));
				}
			}

			return arguments;
		}

		public int GetArgumentPointerCount()
		{
			// Get kernel argument types
			Type[] argTypes = this.Arguments.Values.ToArray();

			// Count pointer arguments
			int count = 0;
			foreach (Type type in argTypes)
			{
				if (type.Name.EndsWith("*"))
				{
					count++;
				}
			}

			return count;
		}




		// UI
		public Dictionary<CLKernel, string> PrecompileAllKernels(bool cache)
		{
			// Get all kernel files
			string[] kernelFiles = this.Files.Keys.ToArray();

			// Precompile all kernels
			Dictionary<CLKernel, string> precompiledKernels = [];
			foreach (string kernelFile in kernelFiles)
			{
				// Compile kernel
				CLKernel? kernel = this.CompileFile(kernelFile);
				if (kernel != null)
				{
					precompiledKernels.Add(kernel.Value, kernelFile);
				}
				else
				{
					this.Log("Error compiling kernel: " + kernelFile, "", 2);
				}
			}

			this.UnloadKernel();

			// Cache
			if (cache)
			{
				this.KernelCache = precompiledKernels;
			}

			return precompiledKernels;
		}

		public string GetLatestKernelFile(string searchName = "")
		{
			string[] files = this.Files.Keys.ToArray();

			// Get all files that contain searchName
			string[] filteredFiles = files.Where(file => file.Contains(searchName, StringComparison.OrdinalIgnoreCase)).ToArray();
			string latestFile = filteredFiles.Select(file => new FileInfo(file))
				.OrderByDescending(file => file.LastWriteTime)
				.FirstOrDefault()?.FullName ?? "";

			// Return latest file
			if (string.IsNullOrEmpty(latestFile))
			{
				this.Log("No kernel files found with name: " + searchName);
				return "";
			}

			return latestFile;
		}



		// Load
		public CLKernel? LoadKernel(string kernelName = "", string filePath = "")
		{
			// Get kernel file path
			if (!string.IsNullOrEmpty(filePath))
			{
				kernelName = Path.GetFileNameWithoutExtension(filePath);
			}
			else
			{
				filePath = Directory.GetFiles(Path.Combine(this.Repopath, "Kernels"), kernelName + "*.cl", SearchOption.AllDirectories).Where(f => Path.GetFileNameWithoutExtension(f).Length == kernelName.Length).FirstOrDefault() ?? "";
			}

			// Compile kernel if not cached
			if (this.Kernel != null && this.KernelFile == filePath)
			{
				this.Log("Kernel already loaded: " + kernelName, "", 1);
				return this.Kernel;
			}

			CLKernel? kernel = this.Kernel = this.CompileFile(filePath);
			this.KernelFile = filePath;

			// Check if kernel is null
			if (this.Kernel == null)
			{
				this.Log("Kernel is null");
				return null;
			}
			else
			{
				// String of args like "(byte*)'pixels', (int)'width', (int)'height'"
				string argNamesString = string.Join(", ", this.Arguments.Keys.Select((arg, i) => $"({this.Arguments.Values.ElementAt(i).Name}) '{arg}'"));
				this.Log("Kernel loaded: '" + kernelName + "'", "", 1);
				// this.Log("Kernel arguments: [" + argNamesString + "]", "", 1);
			}

			// TryAdd to cached
			this.KernelCache.TryAdd(this.Kernel.Value, filePath);

			return kernel;
		}

		public void UnloadKernel()
		{
			// Release kernel
			if (this.Kernel != null)
			{
				CL.ReleaseKernel(this.Kernel.Value);
				this.Kernel = null;
			}

			// Clear kernel file
			this.KernelFile = null;
		}



		

	}
}
