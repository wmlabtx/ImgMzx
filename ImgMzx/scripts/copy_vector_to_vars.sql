-- Copies the vector (BLOB(4096) = 1024 floats) from the first row of `images`
-- into the `vector` column of the first row of `vars`.
--
-- Usage:  sqlite3 "D:\Users\Murad\Spacer\spacer.db" ".read scripts/copy_vector_to_vars.sql"

-- 1) Add the column (run once; fails harmlessly with "duplicate column name" if it already exists).
ALTER TABLE vars ADD COLUMN vector BLOB(4096);

-- 2) Make sure there is a row in `vars` to update.
INSERT INTO vars (maximages)
SELECT 0 WHERE NOT EXISTS (SELECT 1 FROM vars);

-- 3) Copy the blob.
UPDATE vars
   SET vector = (SELECT vector FROM images ORDER BY rowid LIMIT 1)
 WHERE rowid = (SELECT MIN(rowid) FROM vars);

-- 4) Verify: expect 4096 and a matching prefix.
SELECT length(vector)      AS vars_bytes,
       hex(substr(vector, 1, 16)) AS vars_head
  FROM vars ORDER BY rowid LIMIT 1;

SELECT length(vector)      AS images_bytes,
       hex(substr(vector, 1, 16)) AS images_head
  FROM images ORDER BY rowid LIMIT 1;
