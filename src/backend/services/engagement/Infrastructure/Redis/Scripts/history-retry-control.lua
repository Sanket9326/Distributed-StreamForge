-- @common
expect(KEYS[1], 'zset'); expect(KEYS[2], 'zset'); expect(KEYS[3], 'hash'); expect(KEYS[4], 'hash'); expect(KEYS[5], 'zset')
local id = ARGV[1]; local token = ARGV[2]; local action = ARGV[3]; local now = tonumber(ARGV[4])
if redis.call('HGET', KEYS[4], id) ~= token then return 0 end
local member = redis.call('HGET', KEYS[3], id)
if not member or member == 'done' then return 0 end
local lease = redis.call('ZSCORE', KEYS[2], member)
if not lease or tonumber(lease) <= now then return 0 end
if action == 'renew' then redis.call('ZADD', KEYS[2], now + 30000, member); return 1 end
if action ~= 'ack' and action ~= 'release' then error('INVALID_ACTION') end
redis.call('ZREM', KEYS[2], member); redis.call('HDEL', KEYS[4], id)
if action == 'ack' then
  redis.call('HSET', KEYS[3], id, 'done'); redis.call('ZADD', KEYS[5], now, id)
else redis.call('ZADD', KEYS[1], now + tonumber(ARGV[5]), member) end
return 1
