namespace GameHub.Server.Models;

// Модель таблицы связи с составным первичным ключом.
public class GamePlatform
{
    public Guid GameId { get; set; }

    public int PlatformId { get; set; }
}
