namespace GameHub.Server.Models;

// Модель таблицы связи с составным первичным ключом.
public class GameGenre
{
    public Guid GameId { get; set; }

    public int GenreId { get; set; }
}
