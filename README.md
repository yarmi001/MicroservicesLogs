
# 🛡️ Resilient Microservices with Centralized Logging

![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)
![RabbitMQ](https://img.shields.io/badge/RabbitMQ-FF6600?style=for-the-badge&logo=rabbitmq&logoColor=white)
![PostgreSQL](https://img.shields.io/badge/PostgreSQL-316192?style=for-the-badge&logo=postgresql&logoColor=white)
![Docker](https://img.shields.io/badge/Docker-2496ED?style=for-the-badge&logo=docker&logoColor=white)
![Seq](https://img.shields.io/badge/Seq-Logging-00C853?style=for-the-badge&logo=seq&logoColor=white)

Демонстрация **Enterprise-архитектуры** микросервисов на .NET 8.
Проект реализует надежную обработку сообщений, централизованное логирование, мониторинг в реальном времени и автоматическое восстановление после сбоев.

---

## 🏗 Архитектура

Система построена на базе **Worker Services**, общается асинхронно через **RabbitMQ Topic Exchange** и использует **Seq** для агрегации логов.

```mermaid
graph TD
    subgraph "Business Domain"
        P[Producer Service] -->|Task Message| EX_W[Exchange: Direct]
        EX_W --> Q_W[Queue: work_queue]
        Q_W --> C[Consumer Service]
    end

    subgraph "Observability & Resilience"
        P -.->|Log Info/Error| EX_L[Exchange: logs_exchange]
        C -.->|Log Info/Error| EX_L
        
        EX_L -->|#| Q_ALL[Queue: all_logs_queue]
        EX_L -->|error.#| Q_ERR[Queue: critical_errors_queue]
        
        Q_ALL --> L_DB[Logger: DB Consumer]
        Q_ERR --> L_AL[Logger: Alert Consumer]
        
        L_DB --> DB[(PostgreSQL)]
        
        C -- Nack --> DLQ[Dead Letter Queue]
        P -- Serilog --> SEQ(Seq Dashboard)
        C -- Serilog --> SEQ
        L_DB -- Serilog --> SEQ
    end
```

---

## 🚀 Ключевые возможности (Senior Level)

### 🛡️ Reliability & Resilience (Надежность)
*   **Polly Policies:** Реализован паттерн **Retry with Exponential Backoff**. Сервисы не падают при старте, если инфраструктура недоступна, а "умно" ждут восстановления соединения.
*   **Dead Letter Queue (DLQ):** "Ядовитые" сообщения (Poison Messages), вызывающие сбои, не зацикливают систему, а автоматически изолируются в отдельную очередь для анализа.
*   **Worker Services:** Использование `IHostedService` для правильного управления жизненным циклом приложений.

### 🔍 Observability (Наблюдаемость)
*   **Structured Logging:** Использование **Serilog** для структурного логирования.
*   **Seq Dashboard:** Централизованный сбор логов. Удобный UI для поиска, фильтрации и анализа ошибок в реальном времени.

### ✅ Validation & Configuration
*   **Data Validation:** Использование **FluentValidation** для проверки входящих сообщений. Некорректные данные отбрасываются до записи в БД.
*   **Fail Fast:** Валидация конфигурации при старте (`ValidateOnStart`). Сервис не запустится, если забыли указать строку подключения.
*   **Options Pattern:** Строгая типизация настроек через `IOptions<T>`.

### 💾 Data Management
*   **EF Core Migrations:** Управление схемой базы данных через миграции, а не удаление базы.
*   **Topic Exchange:** Гибкая маршрутизация логов. Ошибки попадают в "Алертинг", а все логи — в "Архив".

---

## 🛠 Структура проекта

```text
📂 MicroservicesLogs
├── 📂 Common             # Shared Kernel: RabbitMqLogger, Models, Validators
├── 📂 Producer           # Генерирует задачи, отправляет логи, использует Polly
├── 📂 Consumer           # Обрабатывает задачи, использует DLQ, ручной Ack/Nack
├── 📂 LoggerService      # Слушает 2 очереди, пишет в Postgres, валидирует данные
└── 🐳 docker-compose.yml  # Оркестрация (RabbitMQ, Postgres, Seq, Apps)
```

---

## 🐳 Запуск (Docker)

**1. Чистый старт (с удалением старых данных):**
```bash
docker compose down -v
docker compose up -d --build
```

**2. Проверка состояния:**
```bash
docker ps
```
*Убедитесь, что все контейнеры имеют статус `healthy` или `Up`.*

---

## 📊 Мониторинг и Проверка

### 1. Логи и Графики (Seq) 🌟
Самый удобный способ следить за системой.
*   **URL:** [http://localhost:8081](http://localhost:8081)
*   **Login/Pass:** см. в `docker-compose.yml` (переменная `SEQ_FIRSTRUN_ADMINPASSWORD`).
*   *Здесь вы увидите логи всех трех сервисов в одном месте.*

### 2. Управление очередями (RabbitMQ)
*   **URL:** [http://localhost:15672](http://localhost:15672)
*   **Login/Pass:** `guest` / `guest` (или `admin`/`admin`, см. docker-compose).
*   *Проверьте вкладку Queues: вы увидите `work_queue`, `all_logs_queue`, `critical_errors_queue` и `dead_letter_queue`.*

### 3. Данные в БД (PostgreSQL)
Проверка сохраненных логов через терминал:
```bash
docker compose exec postgres psql -U postgres -d logs_db -c 'SELECT * FROM "Logs" ORDER BY "Timestamp" DESC LIMIT 10;'
```

---

## ⚙️ Конфигурация

Все настройки вынесены в `docker-compose.yml` и используют стандарт .NET (`Section__Property`).

| Переменная | Назначение |
| :--- | :--- |
| `LoggerSettings__ConnectionString` | Доступ к PostgreSQL |
| `LoggerSettings__RabbitMqHost` | Адрес брокера |
| `SEQ_API_KEY` | Токен для отправки логов в Seq |
| `SEQ_FIRSTRUN_ADMINPASSWORD` | Пароль администратора Seq (для первого входа) |

---
