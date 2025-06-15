using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using System.Collections.Concurrent;

namespace SIMDAPI.DataAccess
{
	public class ImageCollection : IDisposable
	{
		private readonly ConcurrentDictionary<Guid, ImgObj> images = [];
		private readonly object lockObj = new();

		public IReadOnlyCollection<ImgObj> Images => this.images.Values.ToList();

		public ImgObj? this[Guid id]
		{
			get
			{
				this.images.TryGetValue(id, out ImgObj? imgObj);
				return imgObj;
			}
		}

		public ImgObj? this[string name]
		{
			get
			{
				lock (this.lockObj)
				{
					return this.images.Values.FirstOrDefault(img => img.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
				}
			}
		}

		public ImgObj? this[int index]
		{
			get
			{
				lock (this.lockObj)
				{
					return this.images.Values.ElementAtOrDefault(index);
				}
			}
		}

		public bool Add(ImgObj imgObj)
		{
			if (imgObj == null)
			{
				throw new ArgumentNullException(nameof(imgObj));
			}
			// TryAdd is a thread-safe operation for ConcurrentDictionary
			return this.images.TryAdd(imgObj.Id, imgObj);
		}

		public void Remove(Guid id)
		{
			if (this.images.TryRemove(id, out ImgObj? imgObj))
			{
				imgObj.Dispose();
			}
		}

		public void Clear()
		{
			lock (this.lockObj)
			{
				foreach (ImgObj imgObj in this.images.Values)
				{
					imgObj.Dispose();
				}
				this.images.Clear();
			}
		}

		public void Dispose()
		{
			this.Clear();
			GC.SuppressFinalize(this);
		}

		public ImgObj? LoadImage(string filePath)
		{
			if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
			{
				Console.WriteLine($"LoadImage: File not found or path empty: {filePath}");
				return null;
			}
			ImgObj imgObj = new(filePath); // Uses the file-path constructor
			if (imgObj.Img == null) // Check if loading was successful within ImgObj
			{
				Console.WriteLine($"LoadImage: Failed to load image data from file: {filePath}");
				return null;
			}

			if (this.Add(imgObj)) // Use the thread-safe Add method
			{
				Console.WriteLine($"Loaded and added image '{imgObj.Name}' (ID: {imgObj.Id}) from file.");
				return imgObj;
			}
			else
			{
				// Dispose the ImgObj if it couldn't be added (e.g., ID collision)
				imgObj.Dispose();
				Console.WriteLine($"Failed to add image '{imgObj.Name}' (ID: {imgObj.Id}). An image with this ID might already exist.");
				return null;
			}
		}

		public ImgObj? PopEmpty(Size? size = null)
		{
			size ??= new Size(1080, 1920);

			ImgObj imgObj = new(new byte[size.Value.Width * size.Value.Height * 4], size.Value.Width, size.Value.Height, "EmptyImage");

			if (this.Add(imgObj)) // Use the thread-safe Add method
			{
				Console.WriteLine($"Created and added empty image '{imgObj.Name}' (ID: {imgObj.Id}) with size {size.Value.Width}x{size.Value.Height}.");
				return imgObj;
			}
			else
			{
				// Dispose the ImgObj if it couldn't be added (e.g., ID collision)
				imgObj.Dispose();
				Console.WriteLine($"Failed to add empty image '{imgObj.Name}' (ID: {imgObj.Id}). An image with this ID might already exist.");
				return null;
			}
		}

	}



	public class ImgObj : IDisposable
	{
		public Guid Id { get; private set; }


		public string Filepath { get; set; } = string.Empty;
		public string Name { get; set; } = string.Empty;


		public Image<Rgba32>? Img { get; set; } = null;
		public int Width { get; set; } = 0;
		public int Height { get; set; } = 0;
		public int Channels { get; set; } = 4;
		public int Bitdepth { get; set; } = 0;


		public long SizeInBytes => this.Width * this.Height * this.Channels * (this.Bitdepth / 8);


		public IntPtr Pointer { get; set; } = IntPtr.Zero;


		public bool OnHost => this.Pointer == IntPtr.Zero && this.Img != null;
		public bool OnDevice => this.Pointer != IntPtr.Zero && this.Img == null;



		public ImgObj(string filePath)
		{
			this.Id = Guid.NewGuid();
			this.Filepath = filePath;
			this.Name = Path.GetFileNameWithoutExtension(filePath);
			
			try
			{
				this.Img = (Image<Rgba32>?) Image<Rgba32>.Load(filePath);
				this.Width = this.Img?.Width ?? 0;
				this.Height = this.Img?.Height ?? 0;
				this.Channels = 4;
				this.Bitdepth = this.Img?.PixelType.BitsPerPixel ?? 0;
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Error loading image {filePath}: {ex.Message}");
				this.Img = null;
			}
		}

		public ImgObj(byte[] rawPixelData, int width, int height, string name = "UnbenanntesBild")
		{
			this.Id = Guid.NewGuid();
			this.Name = name;
			this.Filepath = string.Empty; // Bei rohen Daten gibt es keinen Dateipfad

			try
			{
				// SixLabors.ImageSharp benötigt explizite Breite und Höhe, um aus rohen Daten zu laden
				this.Img = Image.LoadPixelData<Rgba32>(rawPixelData, width, height);
				this.Width = this.Img.Width;
				this.Height = this.Img.Height;
				this.Channels = 4; // Rgba32 hat immer 4 Kanäle
				this.Bitdepth = this.Img.PixelType.BitsPerPixel; // Bits pro Pixel, z.B. 32 für Rgba32
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Fehler beim Erstellen des Bildes aus rohen Daten: {ex.Message}");
				this.Img = null;
			}
		}



		public byte[] GetBytes(bool keepImage = false)
		{
			if (this.Img == null)
			{
				return [];
			}
			
			int bytesPerPixel = this.Img.PixelType.BitsPerPixel / 8;
			long totalBytes = this.Width * this.Height * bytesPerPixel;

			byte[] bytes = new byte[totalBytes];
			
			this.Img.CopyPixelDataTo(bytes);

			if (!keepImage)
			{
				this.Img.Dispose();
				this.Img = null;
			}

			return bytes;
		}

		public Image? SetImage(byte[] bytes, bool keepPointer = false)
		{
			if (this.Img != null)
			{
				this.Img.Dispose();
			}

			try
			{
				this.Img = Image.LoadPixelData<Rgba32>(bytes, this.Width, this.Height);
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Error setting image from bytes: {ex.Message}");
				this.Img = null;
				return null;
			}

			if (!keepPointer)
			{
				this.Pointer = IntPtr.Zero;
			}

			return this.Img;
		}

		public void Dispose()
		{
			if (this.Img != null)
			{
				this.Img.Dispose();
				this.Img = null;
			}
			this.Pointer = IntPtr.Zero;
		}

		public string? ExportToFile(string? filePath = null, string? format = null)
		{
			if (this.Img == null)
			{
				return null;
			}
			
			if (string.IsNullOrEmpty(filePath))
			{
				filePath = Path.Combine(Path.GetTempPath(), $"{this.Name}_{DateTime.Now:yyyyMMdd_HHmmss}.png");
			}

			try
			{
				if (string.IsNullOrEmpty(format) || format.Equals("png", StringComparison.OrdinalIgnoreCase))
				{
					this.Img.SaveAsPng(filePath);
				}
				else if (format.Equals("jpg", StringComparison.OrdinalIgnoreCase) || format.Equals("jpeg", StringComparison.OrdinalIgnoreCase))
				{
					this.Img.SaveAsJpeg(filePath);
				}
				else if (format.Equals("bmp", StringComparison.OrdinalIgnoreCase))
				{
					this.Img.SaveAsBmp(filePath);
				}
				else
				{
					throw new NotSupportedException($"Format '{format}' is not supported.");
				}

				return filePath;
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Error exporting image to file: {ex.Message}");
				return null;
			}
		}

		public async Task<byte[]> GetImageAsFileFormatAsync(IImageEncoder? encoder = null)
		{
			if (this.Img == null)
			{
				return [];
			}
			encoder ??= new PngEncoder();
			using MemoryStream ms = new();
			await this.Img.SaveAsync(ms, encoder); // Asynchronous save to memory stream
			return ms.ToArray();
		}

		public override string ToString()
		{
			return $"{this.Width}x{this.Height} px, {this.Channels} ch., {this.Bitdepth} Bits";
		}

	}
}
