# JOIN - Enterprise .NET 10 Reference Architecture

Hi bud! 👋 I'm Livingstone. After 15+ years of building software, I’ve realized that the difference between a project that scales and one that becomes a nightmare is **Architecture**.

I built **JOIN** as a personal "Gold Standard". It’s not just a repository; it's a production-ready blueprint designed to help you (and the community) master enterprise-grade patterns without falling into common traps.

## 🎯 The Mission: Why SOLID & Clean Architecture?

The core goal of this project is to demonstrate that **SOLID principles** aren't just interview questions—they are the foundation of maintainable software.

By using **Clean Architecture**, we decouple business logic from external tools. Whether you use SQL Server, PostgreSQL, RabbitMQ, or Azure Service Bus, the heart of your application—the **Domain**—remains untouched.


## 🏗️ Architectural Foundations & Patterns

This project implements a robust set of patterns and tools identified as industry best practices (as seen in my latest architecture blueprints):

### 1. Separation of Concerns (Clean Arch)

- **Domain:** Pure business logic, Entities, and **Specification Pattern** to encapsulate query logic.
- **Application:** Use Cases via **CQRS (MediatR)**. Includes **MediatR Pipeline Behaviors** for cross-cutting concerns like validation and logging.
- **Infrastructure:** Implementations for **Persistence (EF Core & Dapper)**, **Messaging (MassTransit + RabbitMQ)**, and **Shared Services (SendGrid)**.
- **Web API:** Orchestration layer with **API Versioning** and documentation via **Swagger & ReDoc**.

### 2. Design Patterns for Enterprise Resilience

- **Result Pattern:** Predictable flow control without throwing exceptions.
- **Unit of Work & Repository:** Consistent data integrity across transactions.
- **Health Checks & UI:** Real-time monitoring of system vitals (Database, Redis, RabbitMQ).
- **Rate Limiting & Timeouts:** Built-in protection to ensure service availability.
- **Fluent Validation:** Robust input validation before reaching the core logic.

### 3. Modern Tech Stack & Observability

- **Framework:** .NET 10 (C# 13) utilizing **Primary Constructors**.
- **Data:** **Dual Persistence** (EF Core for complex writes, Dapper for high-speed reads).
- **Cache:** Distributed caching with **Redis**.
- **Testing:** Unit Testing with **MSTest** and data generation using **Bogus**.
- **Logging:** Structured logging with **Serilog**.

---

## 📂 Project Structure

```plaintext
src/
 ├── 1.Domain          # Entities, Specs, Value Objects (SOLID Core)
 ├── 2.Application     # CQRS (Commands/Queries), DTOs, Handlers, Behaviors
 ├── 3.Infrastructure  # Persistence, EventBus (RabbitMQ), Notifications
 └── 4.Services.WebApi # Controllers, Middlewares, API Versioning, Program.cs
tests/
 ├── UnitTests         # Domain and Application Logic
 └── IntegrationTests  # Infrastructure and End-to-End API flows
```

## 🚀 Getting Started (Coming Soon)

I'm finalizing a `docker-compose` setup so you can lift the entire ecosystem (**SQL Server, Redis, RabbitMQ, MailServer**) with a single command:

```bash
docker-compose up -d
```


💡 Why JOIN?
In a Senior/Lead role, consistency is key. This project demonstrates how to decouple infrastructure from domain logic, making the system easy to test and evolve. Whether you are building a small service or a complex microsystem, these patterns will save you months of refactoring.


Note to my fellow devs: This is a living project based on real-world experience. I'm constantly updating it with the latest .NET features and industry best practices. If you find it useful, give it a ⭐️ and let's build better software together!


Feel free to explore, open an issue, or use it as a base for your next big project. Let's grow together!
