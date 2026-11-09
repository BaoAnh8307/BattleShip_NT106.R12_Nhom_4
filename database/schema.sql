-- Schema DB cho Battleship (SQL Server)
-- Chay 1 lan de tao DB. Chay lai se bao loi vi bang da ton tai, xoa DB di roi chay lai neu can reset.

CREATE DATABASE Battleship;
GO
USE Battleship;
GO

CREATE TABLE Users (
    user_id       INT IDENTITY(1,1) PRIMARY KEY,
    username      NVARCHAR(20)  NOT NULL UNIQUE,  -- collation mac dinh khong phan biet hoa/thuong
    password_hash VARBINARY(32) NOT NULL,
    salt          VARBINARY(16) NOT NULL,
    created_at    DATETIME2     NOT NULL DEFAULT SYSUTCDATETIME()
);

CREATE TABLE Matches (
    match_id    INT IDENTITY(1,1) PRIMARY KEY,
    player1_id  INT NOT NULL REFERENCES Users(user_id),
    player2_id  INT NOT NULL REFERENCES Users(user_id),
    winner_id   INT NULL REFERENCES Users(user_id),
    end_reason  VARCHAR(20) NULL,   -- ALL_SHIPS_SUNK / SURRENDER / DISCONNECT
    total_shots INT NULL,
    started_at  DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    ended_at    DATETIME2 NULL,     -- NULL nghia la van chua ket thuc
    CONSTRAINT CK_Matches_Players CHECK (player1_id <> player2_id)
);

CREATE INDEX IX_Matches_P1 ON Matches(player1_id, ended_at DESC);
CREATE INDEX IX_Matches_P2 ON Matches(player2_id, ended_at DESC);
GO
