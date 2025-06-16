using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using SIMDAPI.OpenCL;
using SIMDAPI.Vulkan;
using SIMDAPI.WebApp.Services;
using SIMDAPI.WebApp.Shared;

namespace SIMDAPI.WebApp
{
	public class Program
	{
		public static async Task Main(string[] args)
		{
			var builder = WebAssemblyHostBuilder.CreateDefault(args);
			builder.RootComponents.Add<App>("#app");
			builder.RootComponents.Add<HeadOutlet>("head::after");

			builder.Services.AddScoped<ImageService>();

			builder.Services.AddSingleton<AppState>();

			builder.Services.AddScoped<OpenClService>();
			builder.Services.AddScoped<VulkanService>();

			builder.Services.AddScoped(sp => new HttpClient
			{
				BaseAddress = new Uri("https://localhost:7265") // 👈 DEIN API-Port!
			});

			await builder.Build().RunAsync();
		}
	}
}
