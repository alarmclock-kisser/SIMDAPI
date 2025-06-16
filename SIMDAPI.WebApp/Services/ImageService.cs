using System.Net.Http.Json;
using System.Text.Json;

namespace SIMDAPI.WebApp.Services;

public class ImageService
{
	private readonly HttpClient Http;
	private readonly Shared.AppState AppState;

	public ImageService(HttpClient http, Shared.AppState appState)
	{
		this.Http = http;
		this.AppState = appState;
	}

	public async Task<string?> UploadImageAsync(Stream stream, string fileName)
	{
		try
		{
			var content = new MultipartFormDataContent();
			var streamContent = new StreamContent(stream);
			content.Add(streamContent, "file", fileName);

			var response = await this.Http.PostAsync("/api/image/upload", content);
			if (!response.IsSuccessStatusCode)
			{
				AppState.Log($"Upload fehlgeschlagen: {response.StatusCode}");
				return null;
			}

			var json = await response.Content.ReadFromJsonAsync<JsonElement>();
			var id = json.GetProperty("id").GetString();
			AppState.Log($"Upload erfolgreich. ID: {id}");
			return id;
		}
		catch (Exception ex)
		{
			AppState.Log($"Fehler beim Upload: {ex.Message}");
			return null;
		}
	}

	public async Task<string?> DownloadImageBase64Async(Guid id)
	{
		try
		{
			var response = await this.Http.GetAsync($"/api/image/{id}/download?format=png");
			if (!response.IsSuccessStatusCode)
			{
				AppState.Log($"Download fehlgeschlagen: {response.StatusCode}");
				return null;
			}

			var bytes = await response.Content.ReadAsByteArrayAsync();
			AppState.Log($"Download erfolgreich (ID: {id}, {bytes.Length} Bytes)");
			return Convert.ToBase64String(bytes);
		}
		catch (Exception ex)
		{
			AppState.Log($"Fehler beim Download: {ex.Message}");
			return null;
		}
	}

	public async Task<bool> ExecuteMandelbrotKernelAsync(Guid id, double zoom = 1.1, string hexColor = "#000000")
	{
		try
		{
			var uri = $"/api/opencl/{id}/ExecuteKernelMandelbrot/mandelbrotPrecise/01?zoom={zoom}&color={hexColor}";
			var response = await this.Http.PostAsync(uri, null);
			AppState.Log($"Kernel-Ausführung für ID {id} → {(response.IsSuccessStatusCode ? "OK" : "FEHLER")}");
			return response.IsSuccessStatusCode;
		}
		catch (Exception ex)
		{
			AppState.Log($"Fehler bei Kernel-Aufruf: {ex.Message}");
			return false;
		}
	}
}
