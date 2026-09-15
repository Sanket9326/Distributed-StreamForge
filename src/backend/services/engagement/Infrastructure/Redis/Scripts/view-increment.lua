-- @common
expect(KEYS[1], 'string'); expect(KEYS[2], 'string')
local ttl = tonumber(ARGV[1])
if not ttl or ttl <= 0 then error('INVALID_TTL') end
local current = redis.call('GET', KEYS[2]) or '0'; decimal(current)
if compare(current, '9223372036854775807') >= 0 then error('COUNT_OVERFLOW') end
if redis.call('SET', KEYS[1], '1', 'NX', 'EX', ttl) then
  redis.call('INCR', KEYS[2]); return {1, redis.call('GET', KEYS[2])}
end
return {0, current}
