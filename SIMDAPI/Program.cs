
using SIMDAPI.DataAccess;
using SIMDAPI.OpenCL;

namespace SIMDAPI
{
    public class Program
    {
        public static void Main(string[] args)
        {
			var MyAllowAllCorsPolicy = "_MyAllowAllCorsPolicy";

			var builder = WebApplication.CreateBuilder(args);

			builder.WebHost.ConfigureKestrel(options =>
			{
                options.Limits.MaxRequestBodySize = 32 * 1024 * 1024;
			});


			// Add services to the container.

			builder.Services.AddControllers();
            // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();

			// Singleton-Registrierung für OpenClService
			builder.Services.AddScoped<OpenClService>();

			// Singleton-Registrierung für VulkanService
			builder.Services.AddScoped<Vulkan.VulkanService>(sp => new Vulkan.VulkanService(-1));

			// Singleton-Registrierung für ImageCollection
			builder.Services.AddScoped<ImageCollection>();

			builder.Services.AddCors(options =>
			{
				options.AddPolicy(name: MyAllowAllCorsPolicy,
					policy =>
					{
						policy
							.WithOrigins("https://localhost:7144")
							.AllowAnyHeader()
							.AllowAnyMethod();
					});
			});


			var app = builder.Build();

			app.UseCors(MyAllowAllCorsPolicy);

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
