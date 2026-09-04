using System.Text.Json;

namespace Gram.Blazor.Shared;

public sealed class ProjectService
{
    private readonly SessionStorageService _sessionStorage;
    private readonly IDatabaseProvider _db;
    private readonly IAuthService _authService;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public List<Project> Projects { get; private set; } = [];
    public event Action? OnChange;

    public ProjectService(
        SessionStorageService sessionStorage,
        IDatabaseProvider db,
        IAuthService authService)
    {
        _sessionStorage = sessionStorage;
        _db = db;
        _authService = authService;

        _authService.OnAuthStateChanged += () =>
        {
            _ = LoadProjectsAsync();
        };

        _ = InitializeAsync();
    }

    public async Task InitializeAsync()
    {
        await LoadProjectsAsync();
    }

    public async Task LoadProjectsAsync()
    {
        await _lock.WaitAsync();
        try
        {
            var user = _authService.CurrentUser;
            var loadedList = new List<Project>();

            if (user.IsAuthenticated && !string.IsNullOrWhiteSpace(user.Id))
            {
                loadedList = await _db.ListProjectsAsync(user.Id);
            }

            if (loadedList.Count == 0)
            {
                loadedList = await _sessionStorage.ListAllProjectsAsync();
            }

            Projects = loadedList;
            OnChange?.Invoke();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ProjectService] Error loading projects: {ex.Message}");
        }
        finally
        {
            _lock.Release();
        }
    }

    public Project? GetProject(string? projectId)
    {
        if (string.IsNullOrWhiteSpace(projectId)) return null;
        return Projects.FirstOrDefault(p => p.Id.Equals(projectId, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<Project> CreateProjectAsync(string name, string? description = null, string? customInstructions = null, string? color = null, string? icon = null)
    {
        var project = new Project
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            Name = string.IsNullOrWhiteSpace(name) ? "New Project" : name.Trim(),
            Description = description ?? "",
            CustomInstructions = customInstructions ?? "",
            Color = color ?? "#8b5cf6",
            Icon = icon ?? "folder",
            Created = DateTimeOffset.UtcNow,
            Updated = DateTimeOffset.UtcNow
        };

        await SaveProjectAsync(project);
        return project;
    }

    public async Task SaveProjectAsync(Project project)
    {
        await _lock.WaitAsync();
        try
        {
            project.Updated = DateTimeOffset.UtcNow;
            var index = Projects.FindIndex(p => p.Id == project.Id);
            if (index >= 0)
            {
                Projects[index] = project;
            }
            else
            {
                Projects.Insert(0, project);
            }

            var user = _authService.CurrentUser;
            if (user.IsAuthenticated && !string.IsNullOrWhiteSpace(user.Id))
            {
                await _db.SaveProjectAsync(project, user.Id);
            }

            await _sessionStorage.SaveProjectAsync(project);
            OnChange?.Invoke();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ProjectService] Error saving project {project.Id}: {ex.Message}");
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task DeleteProjectAsync(string projectId)
    {
        await _lock.WaitAsync();
        try
        {
            var index = Projects.FindIndex(p => p.Id == projectId);
            if (index >= 0)
            {
                Projects.RemoveAt(index);
                var user = _authService.CurrentUser;
                if (user.IsAuthenticated && !string.IsNullOrWhiteSpace(user.Id))
                {
                    await _db.DeleteProjectAsync(projectId, user.Id);
                }
                await _sessionStorage.DeleteProjectAsync(projectId);
                OnChange?.Invoke();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ProjectService] Error deleting project {projectId}: {ex.Message}");
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<ProjectFileInfo?> UploadProjectFileAsync(string projectId, string fileName, Stream contentStream, string contentType = "text/plain")
    {
        var project = GetProject(projectId);
        if (project == null) return null;

        var fileInfo = await _sessionStorage.SaveProjectFileAsync(projectId, fileName, contentStream, contentType);
        
        var existingIndex = project.Files.FindIndex(f => f.FileName.Equals(fileName, StringComparison.OrdinalIgnoreCase));
        if (existingIndex >= 0)
        {
            project.Files[existingIndex] = fileInfo;
        }
        else
        {
            project.Files.Add(fileInfo);
        }

        await SaveProjectAsync(project);
        return fileInfo;
    }

    public async Task<string?> ReadProjectFileTextAsync(string projectId, string fileName)
    {
        return await _sessionStorage.ReadProjectFileTextAsync(projectId, fileName);
    }

    public async Task<bool> DeleteProjectFileAsync(string projectId, string fileName)
    {
        var project = GetProject(projectId);
        if (project == null) return false;

        var removed = project.Files.RemoveAll(f => f.FileName.Equals(fileName, StringComparison.OrdinalIgnoreCase)) > 0;
        await _sessionStorage.DeleteProjectFileAsync(projectId, fileName);
        if (removed)
        {
            await SaveProjectAsync(project);
        }
        return removed;
    }
}
