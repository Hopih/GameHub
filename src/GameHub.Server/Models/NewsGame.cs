namespace GameHub.Server.Models;

// Модель таблицы связи с составным первичным ключом.
public class NewsGame
{
    public Guid NewsId { get; set; }

    public Guid GameId { get; set; }
}
