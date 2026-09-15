-- @common
expect(KEYS[1], 'string')
if ARGV[2] ~= 'replace' and ARGV[2] ~= 'initialize' and ARGV[2] ~= 'maximum' then error('INVALID_MODE') end
local incoming = decimal(ARGV[1]); local current = redis.call('GET', KEYS[1])
if current then decimal(current) end
if ARGV[2] == 'replace' or not current or (ARGV[2] == 'maximum' and compare(incoming, current) > 0) then
  redis.call('SET', KEYS[1], incoming)
end
return redis.call('GET', KEYS[1])
