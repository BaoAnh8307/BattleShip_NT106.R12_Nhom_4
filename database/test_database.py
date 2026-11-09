"""
Test nhanh cho database.py. Can SQL Server dang chay va da chay schema.sql.
Chay: python test_database.py
Dung ten ngau nhien de chay lai nhieu lan khong bi trung.
"""
import random

import database as db

suffix = random.randint(1000, 9999)
u1, u2 = f"test_a{suffix}", f"test_b{suffix}"

a = db.register(u1, "matkhau123")
b = db.register(u2, "matkhau456")
print("dang ky:", a, b)

# trung ten phai bi chan
try:
    db.register(u1, "matkhau123")
    print("LOI: dang ky trung ma khong bao loi")
except db.DbError as e:
    print("trung ten ->", e.code)

# dang nhap dung / sai
print("login dung:", db.login(u1, "matkhau123"))
for ten, mk in [(u1, "saimatkhau"), ("khongtontai", "matkhau123")]:
    try:
        db.login(ten, mk)
        print("LOI: login sai ma van qua")
    except db.DbError as e:
        print("login sai ->", e.code)

# 1 van: a thang
m = db.create_match(a, b)
db.finish_match(m, a, "ALL_SHIPS_SUNK", 47)
db.finish_match(m, b, "SURRENDER", 1)  # goi lan 2, khong duoc ghi de

print("stats a:", db.get_stats(a))   # played 1, wins 1
print("stats b:", db.get_stats(b))   # played 1, wins 0
print("history b:", db.get_history(b))  # result phai la LOSE
