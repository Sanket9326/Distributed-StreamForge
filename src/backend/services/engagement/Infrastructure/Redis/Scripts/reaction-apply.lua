-- @common
expect(KEYS[1], 'set'); expect(KEYS[2], 'set'); expect(KEYS[3], 'hash')
local value = ARGV[2]; local offset = decimal(ARGV[3])
if value ~= 'like' and value ~= 'dislike' and value ~= 'none' then error('INVALID_REACTION') end
local current = redis.call('HGET', KEYS[3], ARGV[1])
if not current or compare(offset, current) >= 0 then
  redis.call('SREM', KEYS[1], ARGV[1]); redis.call('SREM', KEYS[2], ARGV[1])
  if value == 'like' then redis.call('SADD', KEYS[1], ARGV[1]) end
  if value == 'dislike' then redis.call('SADD', KEYS[2], ARGV[1]) end
  redis.call('HSET', KEYS[3], ARGV[1], offset)
end
return {redis.call('SCARD', KEYS[1]), redis.call('SCARD', KEYS[2])}
