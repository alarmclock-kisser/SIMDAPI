using System.Net.Http.Json;
using System.Text.Json;

namespace SIMDAPI.WebApp.Services;

public class ImageService
{
	private readonly HttpClient Http;

	public ImageService(HttpClient http)
	{
		this.Http = http;
	}

	public async Task<string?> UploadImageAsync(Stream stream, string fileName)
	{
		var content = new MultipartFormDataContent();
		var streamContent = new StreamContent(stream);
		content.Add(streamContent, "file", fileName);

		var response = await this.Http.PostAsync("/api/image/upload", content);
		if (!response.IsSuccessStatusCode)
		{
			Console.WriteLine($"Upload failed: {response.StatusCode}");
			return null;
		}

		var json = await response.Content.ReadFromJsonAsync<JsonElement>();
		return json.GetProperty("id").GetString();
	}

	public async Task<string?> DownloadImageBase64Async(Guid id)
	{
		var response = await this.Http.GetAsync($"/api/image/{id}/download?format=png");
		if (!response.IsSuccessStatusCode)
		{
			Console.WriteLine($"Download failed: {response.StatusCode}");
			return null;
		}

		var bytes = await response.Content.ReadAsByteArrayAsync();
		return Convert.ToBase64String(bytes);
	}

	public async Task<bool> ExecuteMandelbrotKernelAsync(Guid id, double zoom = 1.1, string hexColor = "#000000")
	{
		var uri = $"/api/opencl/{id}/ExecuteKernelMandelbrot/mandelbrotPrecise/01?zoom={zoom}&color={hexColor}";
		var response = await this.Http.PostAsync(uri, null);
		return response.IsSuccessStatusCode;
	}

}
