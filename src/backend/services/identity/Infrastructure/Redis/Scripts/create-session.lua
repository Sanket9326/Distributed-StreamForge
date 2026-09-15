-- @common
expect(KEYS[1], 'string'); expect(KEYS[2], 'string')
local ttl = tonumber(ARGV[2])
if not ttl or ttl <= 0 then error('INVALID_TTL') end
if redis.call('SET', KEYS[1], ARGV[1], 'PX', ttl, 'NX') then
  if KEYS[2] ~= KEYS[1] then redis.call('DEL', KEYS[2]) end
  return 1
end
return 0
