
using SIMDAPI.DataAccess;
using SIMDAPI.OpenCL;

namespace SIMDAPI
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.

            builder.Services.AddControllers();
            // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();

			// Singleton-Registrierung für OpenClService
			builder.Services.AddSingleton<OpenClService>();

			// Singleton-Registrierung für VulkanService
			builder.Services.AddSingleton<Vulkan.VulkanService>(sp => new Vulkan.VulkanService(-1));

			// Singleton-Registrierung für ImageCollection
			builder.Services.AddSingleton<ImageCollection>();

			var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            app.UseHttpsRedirection();

            app.UseAuthorization();


            app.MapControllers();

            app.Run();
        }
    }
}
