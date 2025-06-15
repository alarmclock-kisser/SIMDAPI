using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SIMDAPI.Vulkan
{
	public class VulkanExecutioner
	{
		private VulkanService Service;

		public VulkanExecutioner(VulkanService vulkanService)
		{
			this.Service = vulkanService;
		}

		public async void Dispose()
		{
			// GC
			GC.SuppressFinalize(this);
			await Task.CompletedTask;
		}
	}
}
