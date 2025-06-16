namespace SIMDAPI.WebApp.Shared;

public class AppState
{
	public Guid? CurrentImageId { get; set; } = null;



	public event Action? OnImageChanged;

	public void NotifyImageChanged()
	{
		OnImageChanged?.Invoke();
	}



	public bool IsDarkMode { get; private set; } = false;
	public event Action? OnThemeChanged;

	public void ToggleDarkMode()
	{
		this.IsDarkMode = !this.IsDarkMode;
		OnThemeChanged?.Invoke();
	}



	public event Action<string>? OnToast;
	public void Toast(string msg) => OnToast?.Invoke(msg);



	public List<string> Logs { get; } = [];

	public void Log(string msg)
	{
		var line = $"[{DateTime.Now:HH:mm:ss}] {msg}";
		Console.WriteLine(line);
		this.Logs.Add(line);

		if (this.Logs.Count > 200)
		{
			this.Logs.RemoveAt(0);
		}
	}


}
