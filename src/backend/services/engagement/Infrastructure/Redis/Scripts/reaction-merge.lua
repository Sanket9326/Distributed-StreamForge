-- @common
expect(KEYS[1], 'set'); expect(KEYS[2], 'set'); expect(KEYS[3], 'hash')
expect(KEYS[4], 'string'); expect(KEYS[5], 'string')
if (#ARGV - 1) % 3 ~= 0 or (#ARGV - 1) / 3 > 500 then error('INVALID_BATCH') end
if redis.call('GET', KEYS[4]) ~= ARGV[1] or redis.call('GET', KEYS[5]) ~= ARGV[1] then return 0 end
for i = 2, #ARGV, 3 do
  decimal(ARGV[i + 2])
  local current = redis.call('HGET', KEYS[3], ARGV[i])
  if current then decimal(current) end
  if ARGV[i + 1] ~= 'like' and ARGV[i + 1] ~= 'dislike' then error('INVALID_REACTION') end
end
for i = 2, #ARGV, 3 do
  local current = redis.call('HGET', KEYS[3], ARGV[i])
  if not current or compare(ARGV[i + 2], current) >= 0 then
    redis.call('SREM', KEYS[1], ARGV[i]); redis.call('SREM', KEYS[2], ARGV[i])
    redis.call('SADD', ARGV[i + 1] == 'like' and KEYS[1] or KEYS[2], ARGV[i])
    redis.call('HSET', KEYS[3], ARGV[i], ARGV[i + 2])
  end
end
return 1
