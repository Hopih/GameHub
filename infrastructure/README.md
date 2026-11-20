# Qdrant и эмбеддинги GameHub

Из корня репозитория запустите сервисы и дождитесь их готовности.

```powershell
docker compose up -d --wait --wait-timeout 720
```

Qdrant доступен на `http://127.0.0.1:6333`, модель на `http://127.0.0.1:18081`. Порты на хосте можно изменить через `QDRANT_PORT` и `EMBEDDINGS_PORT`. При первом запуске сервис скачивает веса модели. Данные Qdrant и кэш весов сохраняются в именованных томах при пересоздании контейнеров.

Отправляйте описания игр и запросы игроков на один `POST /embed`. Добавляйте к описаниям `passage: `, а к запросам `query: `, в том числе для русского текста. Сервис обрезает входной текст до 512 токенов и возвращает нормализованные векторы по 384 числа. Для коллекции Qdrant используйте `size: 384` и `distance: Cosine`. [Карточка модели](https://huggingface.co/intfloat/multilingual-e5-small)

```powershell
$body = @{
    inputs = @(
        'passage: Explore a medieval open world and fight monsters.'
        'query: игра про охоту на монстров в средневековом открытом мире'
    )
} | ConvertTo-Json

$vectors = Invoke-RestMethod -Method Post -Uri 'http://127.0.0.1:18081/embed' `
    -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($body))
$vectors[0].Count
$vectors[1].Count
```
