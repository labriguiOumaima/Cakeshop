# Cakeshop — Cake Customization & Recommendations

A web application built with **C# and .NET** that combines custom cake ordering with a **Neo4j-powered recommendation system**.

Customers can personalize cakes by selecting layers, flavors, and other available options. Neo4j supports suggestions to help users discover cakes they may enjoy.

## Features

### Cake Customization

- Personalize cakes by selecting layers and flavors.
- Combine available customization options.
- Add personalized cakes to the shopping cart and place orders.

### Recommendations with Neo4j

- Use a graph database to support cake recommendations.
- Use relationships between application data to generate suggestions.
- Help users discover cakes beyond their initial selection.

### Shopping Experience

- Browse the cake catalog.
- View product details.
- Manage the shopping cart.
- Place cake orders.

### Administration

- Manage products and cake listings.
- View and manage customer orders.
- Manage users.
- Monitor store activity through an administration dashboard.

## Tech Stack

| Component | Technologies |
|-----------|--------------|
| Language | C# |
| Framework | .NET, ASP.NET Core MVC |
| Recommendations | Neo4j graph database |
| View Engine | Razor |
| User Interface | HTML, CSS |
| Development Environment | Visual Studio |
| Version Control | Git, GitHub |

## Neo4j Integration

Neo4j provides the graph database capabilities used by the recommendation feature.

By working with relationships between data, the application extends the shopping experience beyond catalog browsing and cake customization to include suggestions for users.

## Project Structure

- `Cakeshopp/` — Application source code.
- `Cakeshopp.sln` — Visual Studio solution.
- `.gitignore` — Version control exclusion rules.

## Architecture

The application follows the **Model–View–Controller (MVC)** pattern:

- **Models** represent application data and business entities.
- **Views** display the shopping and administration interfaces.
- **Controllers** handle requests and coordinate application operations.

Neo4j integration supports the recommendation functionality.

## Local Setup

### Prerequisites

- A .NET SDK compatible with the project.
- Visual Studio with ASP.NET and web development support.
- A running Neo4j instance.
- Any additional database services required by the application configuration.

### Installation

1. Clone the repository.
2. Open `Cakeshopp.sln` in Visual Studio.
3. Restore the required NuGet packages.
4. Configure the application’s database connections.
5. Configure the Neo4j connection URI and credentials.
6. Prepare the database structures and data required by the application.
7. Build and run the project.

## Configuration

Use your own local database settings and Neo4j credentials. Keep passwords and other secrets outside version control.

## Project Purpose

This academic project combines **e-commerce, product customization, and graph-based recommendations**, demonstrating the use of C#/.NET and Neo4j within a cake ordering platform.
