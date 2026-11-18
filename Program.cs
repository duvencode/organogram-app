using OrganogramApp.Components;

namespace OrganogramApp
{
	public class Program
	{
		public static void Main(string[] args)
		{
			// When published, keep the data files next to the executable regardless of the working directory.
			var publishedRoot = File.Exists(Path.Combine(AppContext.BaseDirectory, "EmployeeData.json"))
				? AppContext.BaseDirectory
				: null;
			var builder = WebApplication.CreateBuilder(new WebApplicationOptions
			{
				Args = args,
				ContentRootPath = publishedRoot
			});

			// Add services to the container.
			builder.Services.AddRazorComponents()
				.AddInteractiveServerComponents();
			builder.Services.AddSingleton<OrganogramApp.Services.EmployeeService>();

			var app = builder.Build();

			// Configure the HTTP request pipeline.
			if (!app.Environment.IsDevelopment())
			{
				app.UseExceptionHandler("/Error");
			}

			app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

			app.UseAntiforgery();

			app.MapStaticAssets();
			app.MapRazorComponents<App>()
				.AddInteractiveServerRenderMode();

			// Open the default browser when the published app starts (set "OpenBrowser": false to disable).
			if (!app.Environment.IsDevelopment() && app.Configuration.GetValue("OpenBrowser", true))
			{
				app.Lifetime.ApplicationStarted.Register(() =>
				{
					var url = app.Urls.FirstOrDefault()?
						.Replace("0.0.0.0", "localhost")
						.Replace("[::]", "localhost")
						.Replace("+", "localhost");
					if (string.IsNullOrEmpty(url)) return;
					try
					{
						System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
					}
					catch
					{
						// Ignore if no browser/desktop is available (for example when running as a service).
					}
				});
			}

			app.Run();
		}
	}
}
