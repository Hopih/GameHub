namespace GameHub.Server.Models;

// Модель таблицы связи с составным первичным ключом.
public class UserGenre
{
    public Guid UserId { get; set; }

    public int GenreId { get; set; }
}
