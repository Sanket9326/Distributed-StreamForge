-- @common
expect(KEYS[1], 'zset'); expect(KEYS[2], 'zset'); expect(KEYS[3], 'hash'); expect(KEYS[4], 'hash'); expect(KEYS[5], 'zset')
local now = tonumber(ARGV[1]); local token = ARGV[2]
local expired = redis.call('ZRANGE', KEYS[2], '-inf', now, 'BYSCORE', 'LIMIT', 0, 50)
for _, member in ipairs(expired) do
  local id = cjson.decode(member).retryId
  redis.call('ZREM', KEYS[2], member); redis.call('HDEL', KEYS[4], id)
  redis.call('ZADD', KEYS[1], now, member)
end
local completed = redis.call('ZRANGE', KEYS[5], '-inf', now - 86400000, 'BYSCORE', 'LIMIT', 0, 50)
for _, id in ipairs(completed) do
  redis.call('ZREM', KEYS[5], id); redis.call('HDEL', KEYS[3], id)
end
local due = redis.call('ZRANGE', KEYS[1], '-inf', now, 'BYSCORE', 'LIMIT', 0, 50)
for _, member in ipairs(due) do
  local id = cjson.decode(member).retryId
  redis.call('ZREM', KEYS[1], member)
  redis.call('ZADD', KEYS[2], now + 30000, member)
  redis.call('HSET', KEYS[4], id, token)
end
return due
